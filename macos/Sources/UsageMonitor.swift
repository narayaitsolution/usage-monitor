// Usage Monitor (macOS) - menu bar monitor for Claude (claude CLI), ChatGPT (Codex CLI) and Antigravity (agy CLI) usage limits.
// Build with ./build.sh (requires Xcode Command Line Tools, macOS 12+).
import Cocoa
import SwiftUI
import UserNotifications

// MARK: - Model

struct UsageWindow {
    var label = ""
    var percent: Double?
    var resetAt: Date?
}

struct ProviderStatus {
    var name: String
    var plan: String?
    var account: String?
    var error: String?
    var five = UsageWindow(label: T("5 jam", "5 hours"))
    var week = UsageWindow(label: T("7 hari", "7 days"))
    var moreRows: [UsageWindow] = []   // extra limits, e.g. Claude's weekly limit for one model
    var infos: [String] = []           // extra lines: Claude "Limit resets", ChatGPT "usage limit reset" credits
    var fixedRows = true      // false for providers without the 5h/7d pair (Antigravity)
    var rows: [UsageWindow] { (fixedRows ? [five, week] : []) + moreRows }
    /// Bars in the menu bar icon: 5h & 7d, or the first two limits (Antigravity: one per model group).
    var iconValues: [Double?] {
        if fixedRows { return [five.percent, week.percent] }
        let v = moreRows.prefix(2).map { $0.percent }
        return v.isEmpty ? [nil] : Array(v)
    }
    var keepPrevious = false  // soft error (expired token): keep showing the last good data
    var rateLimited = false   // server answered HTTP 429
    var note: String?         // shown under the rows, e.g. when displaying older data
    var fetchedAt: Date?
}

enum FetchError: Error {
    case http(Int, String)
    case message(String)
}

let refreshInterval: TimeInterval = 5 * 60
let claudeAccent = Color(red: 217 / 255, green: 119 / 255, blue: 87 / 255)
let gptAccent = Color(red: 16 / 255, green: 163 / 255, blue: 127 / 255)
let agyAccent = Color(red: 66 / 255, green: 133 / 255, blue: 244 / 255)
let homeDir = FileManager.default.homeDirectoryForCurrentUser

// MARK: - Language

/// The "language" setting is "" (follow macOS), "id" (Bahasa Indonesia) or "en" (English).
enum Lang {
    static let key = "language"

    static var choice: String {
        let v = UserDefaults.standard.string(forKey: key) ?? ""
        return v == "id" || v == "en" ? v : ""
    }

    static func setChoice(_ c: String) {
        UserDefaults.standard.set(c == "id" || c == "en" ? c : "", forKey: key)
    }

    /// True when the first preferred language of macOS is Indonesian.
    static var systemIsIndonesian: Bool {
        let first = (Locale.preferredLanguages.first ?? "").lowercased()
        return first.hasPrefix("id") || first.hasPrefix("in")
    }

    static var isID: Bool {
        let c = choice
        return c == "id" || (c == "" && systemIsIndonesian)
    }

    /// Locale for day and month names ("Kam 13:00" / "Thu 13:00").
    static var locale: Locale { Locale(identifier: isID ? "id_ID" : "en_US") }
}

/// Text in the current language.
func T(_ indonesian: String, _ english: String) -> String { Lang.isID ? indonesian : english }

// MARK: - Helpers

func parseJSON(_ data: Data) -> [String: Any]? {
    return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
}

func readJSONFile(_ url: URL) -> [String: Any]? {
    guard let d = try? Data(contentsOf: url) else { return nil }
    return parseJSON(d)
}

func num(_ v: Any?) -> Double? {
    if let n = v as? NSNumber { return n.doubleValue }
    if let s = v as? String { return Double(s) }
    return nil
}

func parseISODate(_ s: String) -> Date? {
    // Strip fractional seconds (API returns 6 digits, which ISO8601DateFormatter may reject)
    let cleaned = s.replacingOccurrences(of: "\\.\\d+", with: "", options: .regularExpression)
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime]
    return f.date(from: cleaned)
}

/// GET, or POST with a JSON body when body is given.
func httpGet(_ url: String, _ headers: [String: String], body: [String: Any]? = nil) async throws -> [String: Any] {
    var req = URLRequest(url: URL(string: url)!)
    req.timeoutInterval = 20
    if let body = body {
        req.httpMethod = "POST"
        req.httpBody = try JSONSerialization.data(withJSONObject: body)
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
    }
    req.setValue("application/json", forHTTPHeaderField: "Accept")
    req.setValue(AppInfo.userAgent, forHTTPHeaderField: "User-Agent")
    for (k, v) in headers { req.setValue(v, forHTTPHeaderField: k) }
    let (data, resp) = try await URLSession.shared.data(for: req)
    let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
    guard code == 200 else {
        throw FetchError.http(code, String(data: data.prefix(120), encoding: .utf8) ?? "")
    }
    guard let obj = parseJSON(data) else { throw FetchError.message(T("Respons bukan JSON", "Response is not JSON")) }
    return obj
}

func errorText(_ error: Error, cli: String) -> String {
    if case let FetchError.http(code, body) = error {
        if code == 429 { return T("Dibatasi server (429). Dicoba lagi otomatis.", "Rate limited by the server (429). Retrying automatically.") }
        if code == 401 || code == 403 {
            return T("Token ditolak (\(code)). Buka '\(cli)' agar token di-refresh.", "Token rejected (\(code)). Open '\(cli)' to refresh it.")
        }
        return "HTTP \(code): \(body)"
    }
    if case let FetchError.message(m) = error { return m }
    return T("Koneksi gagal: ", "Connection failed: ") + error.localizedDescription
}

// MARK: - Claude

func claudeConfigDir() -> URL {
    if let d = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"], !d.isEmpty {
        return URL(fileURLWithPath: d)
    }
    return homeDir.appendingPathComponent(".claude")
}

/// On macOS the claude CLI stores credentials in the login Keychain
/// (service "Claude Code-credentials"); fall back to ~/.claude/.credentials.json.
func claudeCredentials() -> [String: Any]? {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/security")
    p.arguments = ["find-generic-password", "-s", "Claude Code-credentials", "-w"]
    let out = Pipe()
    p.standardOutput = out
    p.standardError = Pipe()
    if (try? p.run()) != nil {
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        if p.terminationStatus == 0, let obj = parseJSON(data) { return obj }
    }
    return readJSONFile(claudeConfigDir().appendingPathComponent(".credentials.json"))
}

/// Identity sent with the Claude usage request (see fetchClaude). Version of claude-cli it mimics.
let claudeCliUserAgent = "claude-cli/2.1.326 (external, cli)"

/// Name of a "Limit resets" grant from the limits it clears (same rule as claude-cli).
func grantName(_ gr: [String: Any]) -> String {
    let clears = Set(gr["clears"] as? [String] ?? [])
    if clears.contains("seven_day") { return "Full reset" }
    if clears.contains("seven_day_overage_included") { return "Fable reset" }
    if clears.contains("five_hour") { return T("Reset 5 jam", "5-hour reset") }
    return "Limit reset"
}

func grantExpiry(_ end: Date?) -> String {
    guard let end = end else { return "" }
    let e = end.addingTimeInterval(30)
    let f = DateFormatter()
    f.locale = Lang.locale
    if Calendar.current.isDateInToday(e) {
        f.dateFormat = "HH:mm"
        return T(" · s/d hari ini ", " · until today ") + f.string(from: e)
    }
    f.dateFormat = Lang.isID ? "d MMM HH:mm" : "MMM d HH:mm"
    return T(" · s/d ", " · until ") + f.string(from: e)
}

/// ChatGPT "usage limit resets" with expiry: "↺ Full reset (Weekly + 5 hr) · s/d 2 Des 05:04".
/// Returns nil when the request fails.
func codexResetDetails(_ headers: [String: String]) async -> [String]? {
    guard let root = try? await httpGet("https://chatgpt.com/backend-api/wham/rate-limit-reset-credits", headers) else {
        return nil
    }
    var lines: [String] = []
    for c in root["credits"] as? [[String: Any]] ?? [] where (c["status"] as? String) == "available" {
        let title = (c["title"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "Usage limit reset"
        let end = (c["expires_at"] as? String).flatMap(parseISODate)
        let supported = (c["is_supported_by_plan"] as? Bool) ?? true
        lines.append("↺ " + title + grantExpiry(end) + (supported ? "" : T(" (tidak didukung paket)", " (not supported by plan)")))
    }
    return lines
}

func fetchClaude() async -> ProviderStatus {
    var s = ProviderStatus(name: "Claude")
    guard let creds = claudeCredentials(), let oauth = creds["claudeAiOauth"] as? [String: Any] else {
        s.error = T("Belum login. Jalankan 'claude' lalu /login.", "Not logged in. Run 'claude', then /login.")
        return s
    }
    s.plan = oauth["subscriptionType"] as? String
    guard let token = oauth["accessToken"] as? String, !token.isEmpty else {
        s.error = T("Token tidak ditemukan. Login ulang via claude CLI.", "Token not found. Log in again with the claude CLI.")
        return s
    }
    var cfgs = [homeDir.appendingPathComponent(".claude.json")]
    if let d = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"], !d.isEmpty {
        cfgs.insert(URL(fileURLWithPath: d).appendingPathComponent(".claude.json"), at: 0)
    }
    for cfg in cfgs {
        if let acc = (readJSONFile(cfg)?["oauthAccount"] as? [String: Any])?["emailAddress"] as? String {
            s.account = acc
            break
        }
    }
    if let exp = num(oauth["expiresAt"]), Date(timeIntervalSince1970: exp / 1000) < Date() {
        s.error = T("Token kedaluwarsa. Buka 'claude' sekali agar token di-refresh.", "Token expired. Open 'claude' once to refresh it.")
        return s
    }
    do {
        // The server only lists "Limit resets" (cedar_ember.grants) for official clients,
        // so this request identifies itself as claude-cli, like `/usage` does.
        let root = try await httpGet("https://api.anthropic.com/api/oauth/usage?cedar_ember=1&skip_spend=1",
                                     ["Authorization": "Bearer \(token)", "anthropic-beta": "oauth-2025-04-20",
                                      "x-app": "cli", "User-Agent": claudeCliUserAgent])
        for (key, isFive) in [("five_hour", true), ("seven_day", false)] {
            guard let o = root[key] as? [String: Any] else { continue }
            var w = UsageWindow(label: isFive ? T("5 jam", "5 hours") : T("7 hari", "7 days"))
            w.percent = num(o["utilization"])
            if let r = o["resets_at"] as? String { w.resetAt = parseISODate(r) }
            if isFive { s.five = w } else { s.week = w }
        }
        // "limits" also lists extra limits with their own reset time,
        // e.g. kind "weekly_scoped" = weekly limit for one model (Fable).
        for lim in root["limits"] as? [[String: Any]] ?? [] {
            let kind = lim["kind"] as? String
            if kind == "session" || kind == "weekly_all" { continue } // same as five_hour / seven_day
            let group = lim["group"] as? String
            let base = group == "session" ? T("5 jam", "5 hours") : group == "weekly" ? T("7 hari", "7 days") : (group ?? kind ?? "Limit")
            let scope = lim["scope"] as? [String: Any] ?? [:]
            let name = ["model", "surface"].compactMap { (scope[$0] as? [String: Any])?["display_name"] as? String }.first
            var w = UsageWindow(label: name.map { base + " · " + $0 } ?? base)
            w.percent = num(lim["percent"])
            if let r = lim["resets_at"] as? String { w.resetAt = parseISODate(r) }
            s.moreRows.append(w)
        }
        // "Limit resets" grants (same list as Settings -> Usage on claude.ai)
        let grants = (root["cedar_ember"] as? [String: Any])?["grants"] as? [[String: Any]] ?? []
        for gr in grants {
            let left = Int(num(gr["resets_left"]) ?? 0)
            if left <= 0 { continue }
            let extra = (gr["paused"] as? Bool) == true ? T(" (dijeda)", " (paused)")
                : (gr["usable_now"] as? Bool) == false ? T(" (belum bisa dipakai)", " (not usable yet)") : ""
            let end = (gr["ends_at"] as? String).flatMap(parseISODate)
            s.infos.append("↺ " + grantName(gr) + (left > 1 ? " ×\(left)" : "") + grantExpiry(end) + extra)
        }
    } catch {
        s.error = errorText(error, cli: "claude")
        if case FetchError.http(429, _) = error { s.rateLimited = true }
    }
    return s
}

// MARK: - Codex / ChatGPT

func fetchCodex() async -> ProviderStatus {
    var s = ProviderStatus(name: "ChatGPT")
    let codexHome: URL
    if let d = ProcessInfo.processInfo.environment["CODEX_HOME"], !d.isEmpty {
        codexHome = URL(fileURLWithPath: d)
    } else {
        codexHome = homeDir.appendingPathComponent(".codex")
    }
    guard let auth = readJSONFile(codexHome.appendingPathComponent("auth.json")) else {
        s.error = T("Belum login. Jalankan 'codex login'.", "Not logged in. Run 'codex login'.")
        return s
    }
    let tokens = auth["tokens"] as? [String: Any] ?? [:]
    guard let token = tokens["access_token"] as? String, !token.isEmpty else {
        s.error = T("Codex tidak login dengan akun ChatGPT (mode API key?).", "Codex is not logged in with a ChatGPT account (API key mode?).")
        return s
    }
    var headers = ["Authorization": "Bearer \(token)"]
    if let acc = tokens["account_id"] as? String, !acc.isEmpty { headers["ChatGPT-Account-Id"] = acc }
    do {
        let root = try await httpGet("https://chatgpt.com/backend-api/wham/usage", headers)
        s.plan = root["plan_type"] as? String
        s.account = root["email"] as? String
        let rl = root["rate_limit"] as? [String: Any] ?? [:]
        for key in ["primary_window", "secondary_window"] {
            guard let o = rl[key] as? [String: Any] else { continue }
            let isFive: Bool
            if let secs = num(o["limit_window_seconds"]) { isFive = secs <= 86400 } else { isFive = key == "primary_window" }
            var w = UsageWindow(label: isFive ? T("5 jam", "5 hours") : T("7 hari", "7 days"))
            w.percent = num(o["used_percent"])
            if let r = num(o["reset_at"]) { w.resetAt = Date(timeIntervalSince1970: r) }
            if isFive { s.five = w } else { s.week = w }
        }
        // "Usage limit reset" credits: lets the user reset a reached limit early
        if let credits = root["rate_limit_reset_credits"] as? [String: Any], let available = num(credits["available_count"]) {
            // Details (title + expiry per reset) come from a separate read-only endpoint;
            // only asked when there is something to show. Falls back to the count, like Codex CLI.
            // Never call ".../rate-limit-reset-credits/consume": that USES a reset.
            let details = available > 0 ? await codexResetDetails(headers) : nil
            if let details = details, !details.isEmpty {
                s.infos.append(contentsOf: details)
            } else {
                let usable = num(credits["applicable_available_count"]) ?? 0
                s.infos.append("↺ Usage limit reset: \(Int(available))" + T(" tersedia", " available")
                               + (usable > 0 ? T(" (bisa dipakai sekarang)", " (usable now)") : ""))
            }
        }
    } catch {
        s.error = errorText(error, cli: "codex")
        if case FetchError.http(429, _) = error { s.rateLimited = true }
    }
    return s
}

// MARK: - Antigravity

// agy keeps its Google sign-in in the login Keychain (go-keyring: service "gemini", account "antigravity"),
// as JSON {"token": {"access_token", "expiry", ...}, "id_token_email"}. The token is only read, never
// refreshed: agy refreshes it (about every hour) while it runs.
let cloudCode = "https://cloudcode-pa.googleapis.com/v1internal:"
// The quota endpoints answer 403 to unknown clients, so the requests identify themselves as agy
let antigravityUserAgent = "antigravity"
var agyCache: [String: (project: String, plan: String?)] = [:]   // account -> loadCodeAssist result

func agyCredentials() -> [String: Any]? {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/security")
    p.arguments = ["find-generic-password", "-s", "gemini", "-a", "antigravity", "-w"]
    let out = Pipe()
    p.standardOutput = out
    p.standardError = Pipe()
    guard (try? p.run()) != nil else { return nil }
    let data = out.fileHandleForReading.readDataToEndOfFile()
    p.waitUntilExit()
    guard p.terminationStatus == 0,
          var text = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) else { return nil }
    let prefix = "go-keyring-base64:"
    if text.hasPrefix(prefix), let d = Data(base64Encoded: String(text.dropFirst(prefix.count))),
       let decoded = String(data: d, encoding: .utf8) { text = decoded }
    return parseJSON(Data(text.utf8))
}

func agyWindowLabel(_ window: String?, _ displayName: String?) -> String {
    switch (window ?? "").lowercased() {
    case "weekly": return T("7 hari", "7 days")
    case "daily": return T("1 hari", "1 day")
    case "hourly": return T("1 jam", "1 hour")
    case "five_hour", "5h": return T("5 jam", "5 hours")
    default: return (window?.isEmpty == false ? window : displayName) ?? "Limit"
    }
}

/// "Claude and GPT models" -> "Claude & GPT"
func agyGroupName(_ name: String?) -> String? {
    guard var n = name, !n.isEmpty else { return nil }
    if n.lowercased().hasSuffix(" models") { n = String(n.dropLast(7)) }
    return n.replacingOccurrences(of: " and ", with: " & ")
}

func fetchAntigravity() async -> ProviderStatus {
    var s = ProviderStatus(name: "Antigravity")
    s.fixedRows = false
    guard let cred = agyCredentials() else {
        s.error = T("Belum login. Jalankan 'agy' lalu login dengan akun Google.", "Not logged in. Run 'agy' and sign in with Google.")
        return s
    }
    let tok = cred["token"] as? [String: Any] ?? [:]
    s.account = cred["id_token_email"] as? String
    guard let token = tok["access_token"] as? String, !token.isEmpty else {
        s.error = T("Token tidak ditemukan. Login ulang lewat 'agy'.", "Token not found. Sign in again with 'agy'.")
        return s
    }
    if let e = tok["expiry"] as? String, let exp = parseISODate(e), exp < Date() {
        s.error = T("Token kedaluwarsa. Jalankan 'agy' agar token di-refresh.", "Token expired. Run 'agy' to refresh it.")
        s.keepPrevious = true
        return s
    }
    let headers = ["Authorization": "Bearer \(token)", "User-Agent": antigravityUserAgent]
    let key = s.account ?? ""
    do {
        if agyCache[key] == nil {
            let lca = try await httpGet(cloudCode + "loadCodeAssist", headers, body:
                ["metadata": ["ideType": "ANTIGRAVITY", "platform": "PLATFORM_UNSPECIFIED", "pluginType": "GEMINI"]])
            let plan = (lca["paidTier"] as? [String: Any])?["name"] as? String
                ?? (lca["currentTier"] as? [String: Any])?["name"] as? String
            agyCache[key] = (lca["cloudaicompanionProject"] as? String ?? "", plan)
        }
        let cached = agyCache[key]!
        s.plan = cached.plan
        let root = try await httpGet(cloudCode + "retrieveUserQuotaSummary", headers, body: ["project": cached.project])
        for gr in root["groups"] as? [[String: Any]] ?? [] {
            let group = agyGroupName(gr["displayName"] as? String)
            for b in gr["buckets"] as? [[String: Any]] ?? [] {
                if b["disabled"] as? Bool == true { continue }
                let win = agyWindowLabel(b["window"] as? String, b["displayName"] as? String)
                var w = UsageWindow(label: group.map { win + " · " + $0 } ?? win)
                if let frac = num(b["remainingFraction"]) {
                    w.percent = min(100, max(0, (1 - frac) * 100))
                } else if let amount = num(b["remainingAmount"]) {
                    w.label += T(" (sisa \(Int(amount)))", " (\(Int(amount)) left)")
                }
                w.resetAt = (b["resetTime"] as? String).flatMap(parseISODate)
                s.moreRows.append(w)
            }
        }
        if s.moreRows.isEmpty { s.infos.append(T("Tidak ada batas kuota untuk akun ini.", "No quota limits reported for this account.")) }
    } catch {
        agyCache[key] = nil
        s.error = errorText(error, cli: "agy")
        if case FetchError.http(429, _) = error { s.rateLimited = true }
        if case FetchError.http(401, _) = error { s.keepPrevious = true }
    }
    return s
}

// MARK: - Formatting

/// Same rule for 5h/7d, Claude/ChatGPT: <50 green, 50-75 orange, >75 red.
func levelNSColor(_ pct: Double) -> NSColor {
    if pct > 75 { return NSColor(srgbRed: 229 / 255, green: 72 / 255, blue: 77 / 255, alpha: 1) }
    if pct >= 50 { return NSColor(srgbRed: 240 / 255, green: 136 / 255, blue: 62 / 255, alpha: 1) }
    return NSColor(srgbRed: 63 / 255, green: 185 / 255, blue: 80 / 255, alpha: 1)
}

func levelColor(_ pct: Double) -> Color { Color(nsColor: levelNSColor(pct)) }

func pctText(_ p: Double?) -> String { p.map { "\(Int($0.rounded()))%" } ?? "-" }

/// Compact reset label: "↻ 2j 12m · 13:00" / "↻ 3h 2j · Kam 13:00" (h = hari, j = jam, m = menit);
/// English: "↻ 2h 12m · 13:00" / "↻ 3d 2h · Thu 13:00".
func shortReset(_ date: Date?, now: Date = Date()) -> String {
    guard let date = date else { return "" }
    let secs = date.timeIntervalSince(now)
    if secs <= 0 { return T("sudah reset", "reset done") }
    let total = Int(secs)
    let days = total / 86400, hours = (total % 86400) / 3600, mins = (total % 3600) / 60
    let dd = T("h", "d"), hh = T("j", "h")
    let d: String
    if days >= 1 { d = "\(days)\(dd) \(hours)\(hh)" }
    else if hours >= 1 { d = "\(hours)\(hh) \(mins)m" }
    else { d = "\(max(1, Int((secs / 60).rounded(.up))))m" }
    let r = date.addingTimeInterval(30)
    let f = DateFormatter()
    f.locale = Lang.locale
    f.dateFormat = Calendar.current.isDate(r, inSameDayAs: now) ? "HH:mm" : "EEE HH:mm"
    return "↻ \(d) · \(f.string(from: r))"
}

func resetText(_ date: Date?, now: Date = Date()) -> String {
    guard let date = date else { return "" }
    let secs = date.timeIntervalSince(now)
    if secs <= 0 { return T("Sudah reset - refresh untuk update", "Already reset - refresh to update") }
    let total = Int(secs)
    let days = total / 86400, hours = (total % 86400) / 3600, mins = (total % 3600) / 60
    let d: String
    if days >= 1 { d = "\(days)" + T(" hari ", " d ") + "\(hours)" + T(" jam", " h") }
    else if hours >= 1 { d = "\(hours)" + T(" jam ", " h ") + "\(mins)" + T(" mnt", " min") }
    else { d = "\(max(1, Int((secs / 60).rounded(.up))))" + T(" mnt", " min") }
    let r = date.addingTimeInterval(30)
    let f = DateFormatter()
    f.locale = Lang.locale
    f.dateFormat = Calendar.current.isDate(r, inSameDayAs: now) ? "HH:mm" : (Lang.isID ? "EEE d MMM, HH:mm" : "EEE MMM d, HH:mm")
    return T("Reset dalam ", "Resets in ") + d + "  ·  " + f.string(from: r)
}

// MARK: - Login item (LaunchAgent)

enum LoginItem {
    static let label = "com.usagemonitor.app"
    static var url: URL { homeDir.appendingPathComponent("Library/LaunchAgents/\(label).plist") }
    static var isEnabled: Bool { FileManager.default.fileExists(atPath: url.path) }
    static func set(_ on: Bool) {
        if on {
            let exe = Bundle.main.executablePath ?? CommandLine.arguments[0]
            let plist: NSDictionary = ["Label": label, "ProgramArguments": [exe], "RunAtLoad": true]
            try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            plist.write(to: url, atomically: true)
        } else {
            try? FileManager.default.removeItem(at: url)
        }
    }
}

// MARK: - Providers

struct ProviderDef: Identifiable, Equatable {
    let id: String
    let title: String
    let menuTitle: String
    let accent: Color
    let fetch: () async -> ProviderStatus
    let installed: () -> Bool
    static func == (a: ProviderDef, b: ProviderDef) -> Bool { a.id == b.id }
}

func dirExists(_ url: URL) -> Bool {
    var isDir: ObjCBool = false
    return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) && isDir.boolValue
}

/// GUI apps don't inherit the shell PATH, so also look in the usual install locations.
func commandExists(_ name: String) -> Bool {
    var dirs = (ProcessInfo.processInfo.environment["PATH"] ?? "").split(separator: ":").map(String.init)
    dirs += ["/opt/homebrew/bin", "/usr/local/bin",
             homeDir.appendingPathComponent(".local/bin").path,
             homeDir.appendingPathComponent(".npm-global/bin").path,
             homeDir.appendingPathComponent(".claude/local").path]
    return dirs.contains { FileManager.default.isExecutableFile(atPath: ($0 as NSString).appendingPathComponent(name)) }
}

func codexHomeDir() -> URL {
    if let d = ProcessInfo.processInfo.environment["CODEX_HOME"], !d.isEmpty { return URL(fileURLWithPath: d) }
    return homeDir.appendingPathComponent(".codex")
}

let allProviders: [ProviderDef] = [
    ProviderDef(id: "claude", title: "Claude", menuTitle: "Claude (claude-cli)", accent: claudeAccent,
                fetch: { await fetchClaude() },
                installed: { dirExists(claudeConfigDir()) || commandExists("claude") }),
    ProviderDef(id: "codex", title: "ChatGPT", menuTitle: "ChatGPT (Codex)", accent: gptAccent,
                fetch: { await fetchCodex() },
                installed: { dirExists(codexHomeDir()) || commandExists("codex") }),
    ProviderDef(id: "antigravity", title: "Antigravity", menuTitle: "Antigravity (agy)", accent: agyAccent,
                fetch: { await fetchAntigravity() },
                installed: { dirExists(homeDir.appendingPathComponent(".gemini/antigravity-cli")) || commandExists("agy") }),
]

/// Providers whose CLI is installed. If none is detected, all are offered so the "belum login" hints show.
func installedProviders() -> [ProviderDef] {
    let found = allProviders.filter { $0.installed() }
    return found.isEmpty ? allProviders : found
}

// MARK: - Store

@MainActor
final class Store: ObservableObject {
    @Published var data: [String: ProviderStatus] = [:]
    @Published var installed: [ProviderDef] = installedProviders()
    @Published var hidden: Set<String> = Set(UserDefaults.standard.stringArray(forKey: "hiddenProviders") ?? [])
    @Published var updated: Date?
    @Published var busy = false
    @Published var now = Date()
    @Published var loginEnabled = LoginItem.isEnabled
    /// Changes when the language is switched, so the views redraw their texts.
    @Published var language = Lang.choice
    var pending = false
    var onUpdate: (@MainActor () -> Void)?

    /// Installed providers that are ticked. Never empty.
    var shown: [ProviderDef] {
        let list = installed.filter { !hidden.contains($0.id) }
        return list.isEmpty ? Array(installed.prefix(1)) : list
    }

    func redetect() {
        let now = installedProviders()
        if now.map({ $0.id }) != installed.map({ $0.id }) {
            installed = now
            onUpdate?()
            if shown.contains(where: { data[$0.id] == nil }) { refresh() }
        }
    }

    func toggle(_ p: ProviderDef) {
        let isShown = shown.contains(p)
        if isShown {
            if shown.count == 1 { NSSound.beep(); return } // at least one
            hidden.insert(p.id)
        } else {
            hidden.remove(p.id)
        }
        UserDefaults.standard.set(Array(hidden), forKey: "hiddenProviders")
        onUpdate?()
        if !isShown && data[p.id] == nil && canFetch(p) { refresh() }
    }

    // Back-off after HTTP 429: skip that provider for 5, 10, 20, then max 30 minutes.
    var nextAllowed: [String: Date] = [:]
    var backoffMin: [String: Int] = [:]

    func canFetch(_ p: ProviderDef) -> Bool {
        guard let t = nextAllowed[p.id] else { return true }
        return Date() >= t
    }

    func store(_ id: String, _ r: ProviderStatus) {
        var r = r
        if r.rateLimited {
            let mins = backoffMin[id].map { min(30, $0 * 2) } ?? 5
            backoffMin[id] = mins
            let retry = Date().addingTimeInterval(TimeInterval(mins * 60))
            nextAllowed[id] = retry
            let f = DateFormatter()
            f.dateFormat = "HH:mm"
            if var prev = data[id], prev.error == nil {
                // keep showing the last good data
                prev.note = T("Dibatasi server (429) · data ", "Rate limited (429) · data ") + (prev.fetchedAt.map { f.string(from: $0) } ?? "-")
                    + T(" · coba lagi ", " · retry ") + f.string(from: retry)
                data[id] = prev
                return
            }
            r.error = T("Dibatasi server (429). Dicoba lagi ", "Rate limited by the server (429). Retrying at ") + f.string(from: retry) + "."
        } else if r.keepPrevious, var prev = data[id], prev.error == nil {
            // soft error (expired token): keep showing the last good data
            let f = DateFormatter()
            f.dateFormat = "HH:mm"
            prev.note = (r.error ?? "") + T(" · data ", " · data from ") + (prev.fetchedAt.map { f.string(from: $0) } ?? "-")
            data[id] = prev
            return
        } else {
            backoffMin[id] = nil
            nextAllowed[id] = nil
            r.fetchedAt = Date()
        }
        data[id] = r
    }

    func refresh() {
        guard !busy else { pending = true; return }
        let provs = shown.filter { canFetch($0) }
        if provs.isEmpty { onUpdate?(); return } // everything is backing off
        busy = true
        pending = false
        Task {
            var results: [String: ProviderStatus] = [:]
            await withTaskGroup(of: (String, ProviderStatus).self) { group in
                for p in provs { group.addTask { (p.id, await p.fetch()) } }
                for await (id, st) in group { results[id] = st }
            }
            for (k, v) in results { self.store(k, v) }
            self.updated = Date()
            self.now = Date()
            self.busy = false
            self.onUpdate?()
            if self.pending { self.refresh() }
        }
    }

    func setLogin(_ on: Bool) {
        LoginItem.set(on)
        loginEnabled = LoginItem.isEnabled
    }

    /// Saves the language; texts from the last fetch are redone by a refresh.
    func setLanguage(_ code: String) {
        let wasID = Lang.isID
        Lang.setChoice(code)
        language = Lang.choice
        guard Lang.isID != wasID else { return }
        for k in Array(data.keys) { data[k]?.note = nil }
        onUpdate?()
        refresh()
    }
}

// MARK: - Views

/// One compact row:  label .......... ↻ 2j 12m · 13:00   38%
///                   [=====-------------------------------]
struct UsageRow: View {
    let window: UsageWindow
    let now: Date

    var body: some View {
        let pct = window.percent
        let color = pct.map(levelColor) ?? Color.gray
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Text(window.label).font(.system(size: 11.5)).foregroundColor(.secondary)
                Spacer(minLength: 4)
                Text(shortReset(window.resetAt, now: now)).font(.system(size: 10.5)).foregroundColor(.secondary)
                Text(pctText(pct))
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor((pct ?? 0) >= 50 ? color : .primary)
                    .frame(minWidth: 32, alignment: .trailing)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.gray.opacity(0.3))
                    if let p = pct, p > 0 {
                        Capsule().fill(color)
                            .frame(width: max(5, geo.size.width * CGFloat(min(100, p)) / 100))
                    }
                }
            }
            .frame(height: 5)
        }
    }
}

struct ProviderCard: View {
    let title: String
    let accent: Color
    let status: ProviderStatus?
    let busy: Bool
    let now: Date

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack(spacing: 6) {
                Circle().fill(accent).frame(width: 8, height: 8)
                Text(title).font(.system(size: 13, weight: .semibold))
                if let plan = status?.plan, !plan.isEmpty {
                    Text(plan.uppercased())
                        .font(.system(size: 10, weight: .medium))
                        .padding(.horizontal, 4).padding(.vertical, 1)
                        .background(accent.opacity(0.3))
                        .cornerRadius(4)
                }
                Spacer(minLength: 8)
                if let acc = status?.account {
                    Text(acc).font(.system(size: 11)).foregroundColor(.secondary)
                        .lineLimit(1).truncationMode(.middle)
                }
            }
            if let s = status {
                if let err = s.error {
                    Text(err).font(.system(size: 12)).foregroundColor(.red)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    ForEach(Array(s.rows.enumerated()), id: \.offset) { _, row in
                        UsageRow(window: row, now: now)
                    }
                    ForEach(s.infos, id: \.self) { info in
                        Text(info).font(.system(size: 10.5)).foregroundColor(.secondary)
                    }
                    if let note = s.note {
                        Text("⚠ " + note).font(.system(size: 11)).foregroundColor(.orange)
                    }
                }
            } else {
                Text(busy ? T("Memuat…", "Loading…") : T("Belum ada data", "No data yet")).font(.system(size: 12)).foregroundColor(.secondary)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.primary.opacity(0.06))
        .cornerRadius(7)
    }
}

struct PopupView: View {
    @ObservedObject var store: Store
    var onInfo: @MainActor () -> Void = {}
    var onDonors: @MainActor () -> Void = {}
    var onSettings: @MainActor () -> Void = {}

    var footer: String {
        guard let u = store.updated else { return T("Belum ada data", "No data yet") }
        let f = DateFormatter()
        f.dateFormat = "HH:mm"
        return T("Diperbarui ", "Updated ") + f.string(from: u) + T("  ·  tiap 5 menit", "  ·  every 5 minutes")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack(spacing: 4) {
                Text("Usage Monitor").font(.system(size: 13, weight: .semibold))
                Spacer()
                Button { onDonors() } label: {
                    Image(systemName: "heart.fill").font(.system(size: 13)).foregroundColor(.pink)
                }
                .buttonStyle(.borderless)
                .help(T("Donatur & donasi", "Donors & donate"))
                Button { store.refresh() } label: {
                    Image(systemName: "arrow.clockwise").font(.system(size: 13, weight: .medium))
                }
                .buttonStyle(.borderless)
                .disabled(store.busy)
                .help(store.busy ? T("Memuat…", "Loading…") : "Refresh")
                Button { onInfo() } label: {
                    Image(systemName: "info.circle").font(.system(size: 14))
                }
                .buttonStyle(.borderless)
                .help(T("Tentang & update", "About & update"))
                // Gear: the same menu as a right-click on the menu bar icon
                Button { onSettings() } label: {
                    Image(systemName: "gearshape").font(.system(size: 14))
                }
                .buttonStyle(.borderless)
                .help(T("Pengaturan", "Settings"))
            }
            ForEach(store.shown) { p in
                ProviderCard(title: p.title, accent: p.accent, status: store.data[p.id], busy: store.busy, now: store.now)
            }
            Text(footer).font(.system(size: 11)).foregroundColor(.secondary)
            Divider()
            HStack {
                Toggle(T("Jalankan saat login", "Run at login"), isOn: Binding(get: { store.loginEnabled },
                                                                              set: { store.setLogin($0) }))
                    .toggleStyle(.checkbox)
                    .font(.system(size: 12))
                Spacer()
                Button(T("Keluar", "Quit")) { NSApp.terminate(nil) }
            }
        }
        .padding(10)
        .frame(width: 300)
    }
}

// MARK: - Menu bar icon

/// Two bars (5h, 7d) per shown provider, stacked in menu order.
func makeIcon(_ statuses: [ProviderStatus?]) -> NSImage {
    let width: CGFloat = 18, height: CGFloat = 16
    let groups: [[Double?]] = statuses.map { p in
        guard let p = p, p.error == nil else { return [nil, nil] }
        return p.iconValues
    }
    let n = CGFloat(max(1, groups.count))
    let bars = CGFloat(max(1, groups.reduce(0) { $0 + $1.count }))
    let gap: CGFloat = 1, groupGap: CGFloat = bars > 4 ? 1 : 2
    let fixed = (bars - n) * gap + (n - 1) * groupGap
    let barH = max(1, min(7, floor((height - fixed) / bars)))
    let img = NSImage(size: NSSize(width: width, height: height), flipped: true) { _ in
        var y = max(0, floor((height - (bars * barH + fixed)) / 2))
        for (p, values) in zip(statuses, groups) {
            let err = p?.error != nil
            for pct in values {
                let track = err ? NSColor.systemRed.withAlphaComponent(0.4) : NSColor.gray.withAlphaComponent(0.5)
                track.setFill()
                NSRect(x: 0, y: y, width: width, height: barH).fill()
                if !err, let v = pct {
                    var w = width * CGFloat(min(100, max(0, v))) / 100
                    if v > 0 && w < 1 { w = 1 }
                    levelNSColor(v).setFill()
                    NSRect(x: 0, y: y, width: w, height: barH).fill()
                }
                y += barH + gap
            }
            y += groupGap - gap
        }
        return true
    }
    img.isTemplate = false
    return img
}

func tipLine(_ title: String, _ p: ProviderStatus?) -> String {
    guard let p = p else { return title + T(": memuat…", ": loading…") }
    if p.error != nil { return "\(title): error" }
    if !p.fixedRows {
        let r = p.rows
        if r.isEmpty { return "\(title) -" }
        return "\(title) 7d \(pctText(r[0].percent))" + (r.count > 1 ? " | \(pctText(r[1].percent))" : "")
    }
    return "\(title) 5h \(pctText(p.five.percent)) | 7d \(pctText(p.week.percent))"
}

// MARK: - App info & update

enum AppInfo {
    static let name = "Usage Monitor"
    /// Major.Minor: Major naik untuk fitur baru, Minor untuk perubahan pada fitur yang ada.
    static let version = "3.0"
    static let repo = "narayaitsolution/usage-monitor"
    static let repoURL = "https://github.com/\(repo)"
    static let userAgent = "UsageMonitor/\(version)"
    /// Donor list and donation link, edited in the repository without a new release.
    static let donorsURL = "https://raw.githubusercontent.com/\(repo)/main/donors.json"
    /// Used when donors.json can't be loaded or has no donate_url.
    static let donateURL = "https://lynk.id/narayait/rp8r1z3rop0w/checkout"
}

struct ReleaseInfo {
    var version: String
    var tag: String
    var notes: String
    var pageURL: String
    var assetName: String?
    var assetURL: URL?
    var isSource = false   // no macOS file in the release: the source code zip is offered instead
}

func updateErrorText(_ error: Error) -> String {
    if case let FetchError.message(m) = error { return m }
    if case let FetchError.http(code, _) = error { return "HTTP \(code)" }
    return error.localizedDescription
}

enum Updater {
    static let skippedKey = "skippedVersion"

    /// "v.1.0.0" -> "1.0.0", "v2.1" -> "2.1"
    static func normalize(_ tag: String) -> String? {
        guard let r = tag.range(of: "\\d+(\\.\\d+)*", options: .regularExpression) else { return nil }
        return String(tag[r])
    }

    /// Compares version numbers part by part: "2.0" equals "2.0.0", "2.1" is newer, "10.0" beats "9.9".
    static func compare(_ a: String, _ b: String) -> Int {
        let pa = a.split(separator: ".").map { Int($0) ?? 0 }
        let pb = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(pa.count, pb.count) {
            let x = i < pa.count ? pa[i] : 0
            let y = i < pb.count ? pb[i] : 0
            if x != y { return x < y ? -1 : 1 }
        }
        return 0
    }

    static func isNewer(_ r: ReleaseInfo) -> Bool { compare(r.version, AppInfo.version) > 0 }

    static func isSkipped(_ r: ReleaseInfo) -> Bool {
        guard let s = UserDefaults.standard.string(forKey: skippedKey) else { return false }
        return compare(s, r.version) == 0
    }

    static func skip(_ r: ReleaseInfo) { UserDefaults.standard.set(r.version, forKey: skippedKey) }

    /// Latest published release on GitHub (drafts and pre-releases are ignored by the API).
    static func fetchLatest() async throws -> ReleaseInfo {
        let root: [String: Any]
        do {
            root = try await httpGet("https://api.github.com/repos/\(AppInfo.repo)/releases/latest", [:])
        } catch FetchError.http(let code, _) {
            if code == 404 { throw FetchError.message(T("Belum ada rilis di GitHub.", "No release on GitHub yet.")) }
            if code == 403 || code == 429 {
                throw FetchError.message(T("GitHub membatasi permintaan. Coba lagi nanti.", "GitHub is rate limiting requests. Try again later."))
            }
            throw FetchError.message(T("GitHub membalas HTTP ", "GitHub answered HTTP ") + "\(code).")
        } catch {
            throw FetchError.message(T("Koneksi gagal: ", "Connection failed: ") + error.localizedDescription)
        }
        let tag = root["tag_name"] as? String ?? ""
        guard let version = normalize(tag) ?? normalize(root["name"] as? String ?? "") else {
            throw FetchError.message(T("Tag rilis tidak berisi nomor versi: ", "Release tag has no version number: ") + tag)
        }
        var r = ReleaseInfo(version: version, tag: tag,
                            notes: (root["body"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines),
                            pageURL: root["html_url"] as? String ?? AppInfo.repoURL + "/releases")
        // File for macOS: a release file with "mac" in its name (.dmg > .zip > other)
        var best = 0
        for a in root["assets"] as? [[String: Any]] ?? [] {
            let name = a["name"] as? String ?? ""
            let n = name.lowercased()
            guard n.contains("mac") || n.contains("darwin") else { continue }
            let score = n.hasSuffix(".dmg") ? 3 : n.hasSuffix(".zip") ? 2 : 1
            if score > best, let u = (a["browser_download_url"] as? String).flatMap({ URL(string: $0) }) {
                best = score
                r.assetName = name
                r.assetURL = u
            }
        }
        if r.assetURL == nil, let z = (root["zipball_url"] as? String).flatMap({ URL(string: $0) }) {
            r.assetName = "usage-monitor-\(version)-source.zip"
            r.assetURL = z
            r.isSource = true
        }
        return r
    }

    static func downloadsDir() -> URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? homeDir.appendingPathComponent("Downloads")
    }

    /// "UsageMonitor.zip" -> "UsageMonitor (1).zip" when the name is taken, like a browser does.
    static func uniqueURL(_ dir: URL, _ name: String) -> URL {
        let stem = (name as NSString).deletingPathExtension
        let ext = (name as NSString).pathExtension
        var url = dir.appendingPathComponent(name)
        var i = 1
        while FileManager.default.fileExists(atPath: url.path) {
            url = dir.appendingPathComponent("\(stem) (\(i))" + (ext.isEmpty ? "" : ".\(ext)"))
            i += 1
        }
        return url
    }

    /// Downloads the release file into the Downloads folder.
    static func download(_ r: ReleaseInfo) async throws -> URL {
        guard let src = r.assetURL, let name = r.assetName else { throw FetchError.message(T("Rilis tidak berisi file.", "The release has no file.")) }
        var req = URLRequest(url: src)
        req.timeoutInterval = 60
        req.setValue(AppInfo.userAgent, forHTTPHeaderField: "User-Agent")
        let (tmp, resp) = try await URLSession.shared.download(for: req)
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard code == 200 else {
            try? FileManager.default.removeItem(at: tmp)
            throw FetchError.message(T("Unduhan gagal", "Download failed") + " (HTTP \(code)).")
        }
        let dir = downloadsDir()
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let dest = uniqueURL(dir, (name as NSString).lastPathComponent)
        try FileManager.default.moveItem(at: tmp, to: dest)
        return dest
    }
}

/// Moves settings and the login item from the old "UsageTray" name (com.usagetray.app).
func migrateFromUsageTray() {
    let d = UserDefaults.standard
    if d.object(forKey: "hiddenProviders") == nil,
       let old = UserDefaults(suiteName: "com.usagetray.app")?.stringArray(forKey: "hiddenProviders") {
        d.set(old, forKey: "hiddenProviders")
    }
    let oldPlist = homeDir.appendingPathComponent("Library/LaunchAgents/com.usagetray.app.plist")
    if FileManager.default.fileExists(atPath: oldPlist.path) {
        try? FileManager.default.removeItem(at: oldPlist)
        LoginItem.set(true)
    }
}

@MainActor
final class UpdateModel: ObservableObject {
    enum Phase { case offer, downloading, done(URL) }
    let release: ReleaseInfo
    @Published var phase = Phase.offer
    @Published var error: String?
    var onClose: (@MainActor () -> Void)?
    var onQuit: (@MainActor () -> Void)?

    init(_ r: ReleaseInfo) { release = r }

    func startDownload() {
        guard release.assetURL != nil else { return }
        error = nil
        phase = .downloading
        Task {
            do {
                let url = try await Updater.download(release)
                self.phase = .done(url)
            } catch let e {
                self.error = updateErrorText(e)
                self.phase = .offer
            }
        }
    }
}

@MainActor
final class AboutModel: ObservableObject {
    @Published var status = T("Update dicek otomatis setiap aplikasi dibuka.", "Updates are checked automatically every time the app starts.")
    @Published var statusColor = Color.secondary
    @Published var checking = false
    var onFound: (@MainActor (ReleaseInfo) -> Void)?
    var onClose: (@MainActor () -> Void)?

    func check() {
        checking = true
        status = T("Memeriksa update…", "Checking for updates…")
        statusColor = .secondary
        Task {
            do {
                let r = try await Updater.fetchLatest()
                self.checking = false
                if Updater.isNewer(r) {
                    self.status = T("Versi \(r.version) tersedia.", "Version \(r.version) is available.")
                    self.statusColor = .accentColor
                    self.onFound?(r)
                } else {
                    self.status = T("✓ Anda sudah memakai versi terbaru", "✓ You have the latest version") + " (\(AppInfo.version))."
                    self.statusColor = .green
                }
            } catch let e {
                self.checking = false
                self.status = T("Gagal memeriksa update: ", "Update check failed: ") + updateErrorText(e)
                self.statusColor = .red
            }
        }
    }
}

/// Sample bars for the app logo in the About window.
func logoImage() -> NSImage {
    var a = ProviderStatus(name: "")
    a.five.percent = 35
    a.week.percent = 62
    var b = ProviderStatus(name: "")
    b.five.percent = 28
    b.week.percent = 85
    return makeIcon([a, b])
}

struct UpdateView: View {
    @ObservedObject var model: UpdateModel
    var r: ReleaseInfo { model.release }

    /// Folder that holds the running UsageMonitor.app
    var appFolder: String { Bundle.main.bundleURL.deletingLastPathComponent().path }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            switch model.phase {
            case .offer: offer
            case .downloading: downloading
            case .done(let url): done(url)
            }
        }
        .padding(18)
        .frame(width: 400)
    }

    var offer: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(T("Update tersedia", "Update available")).font(.system(size: 17, weight: .semibold))
            Text(T("\(AppInfo.name) \(r.version) sudah tersedia. Versi Anda: \(AppInfo.version).",
                   "\(AppInfo.name) \(r.version) is available. You have version \(AppInfo.version)."))
                .foregroundColor(.secondary)
            Text(T("Yang baru di versi \(r.version):", "What's new in version \(r.version):")).font(.system(size: 13, weight: .semibold))
            ScrollView {
                Text(r.notes.isEmpty ? T("(Tidak ada catatan rilis.)", "(No release notes.)") : r.notes)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
            }
            .frame(height: 150)
            .background(Color.primary.opacity(0.06))
            .cornerRadius(6)
            if r.isSource {
                Text(T("Rilis ini tidak berisi file khusus macOS, jadi yang diunduh adalah kode sumbernya.",
                       "This release has no macOS file, so the source code is downloaded instead."))
                    .font(.system(size: 11)).foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let e = model.error {
                Text(e).font(.system(size: 12)).foregroundColor(.red)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(T("Skip: tawaran untuk versi \(r.version) tidak muncul lagi. Tutup jendela ini untuk diingatkan lagi saat aplikasi dibuka berikutnya.",
                   "Skip: version \(r.version) won't be offered again. Close this window to be reminded the next time the app starts."))
                .font(.system(size: 11)).foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("Skip") {
                    Updater.skip(r)
                    model.onClose?()
                }
                Button("Update") {
                    if r.assetURL == nil {
                        if let u = URL(string: r.pageURL) { NSWorkspace.shared.open(u) }
                        model.onClose?()
                    } else {
                        model.startDownload()
                    }
                }
                .keyboardShortcut(.defaultAction)
            }
        }
    }

    var downloading: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(T("Mengunduh update…", "Downloading update…")).font(.system(size: 17, weight: .semibold))
            Text((r.assetName ?? "") + T(" → folder Downloads", " → Downloads folder")).foregroundColor(.secondary)
            ProgressView().progressViewStyle(.linear)
        }
    }

    func done(_ url: URL) -> some View {
        let file = url.lastPathComponent
        let step2 = r.isSource
            ? T("2. Ekstrak \(file), jalankan ./build.sh di folder macos, lalu ganti UsageMonitor.app di \(appFolder) dengan hasil build.",
                "2. Extract \(file), run ./build.sh in the macos folder, then replace UsageMonitor.app in \(appFolder) with the build.")
            : T("2. Buka/ekstrak \(file), lalu ganti UsageMonitor.app di \(appFolder) dengan yang baru.",
                "2. Open/extract \(file), then replace UsageMonitor.app in \(appFolder) with the new one.")
        return VStack(alignment: .leading, spacing: 10) {
            Text(T("Update berhasil diunduh", "Update downloaded")).font(.system(size: 17, weight: .semibold))
            Text(T("File tersimpan di:", "Saved to:")).foregroundColor(.secondary)
            Text(url.path)
                .font(.system(size: 11))
                .textSelection(.enabled)
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.primary.opacity(0.06))
                .cornerRadius(6)
            Text(T("\(AppInfo.name) adalah aplikasi portable, jadi pindahkan file update secara manual:",
                   "\(AppInfo.name) is a portable app, so move the update into place yourself:"))
                .fixedSize(horizontal: false, vertical: true)
            Text(T("1. Keluar dari \(AppInfo.name) (tombol di bawah).", "1. Quit \(AppInfo.name) (button below).")
                 + "\n" + step2 + "\n" + T("3. Jalankan UsageMonitor.app lagi.", "3. Open UsageMonitor.app again."))
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button(T("Tutup", "Close")) { model.onClose?() }
                Button(T("Keluar dari ", "Quit ") + AppInfo.name) { model.onQuit?() }
                Button(T("Tampilkan di Finder", "Show in Finder")) { NSWorkspace.shared.activateFileViewerSelecting([url]) }
                    .keyboardShortcut(.defaultAction)
            }
        }
    }
}

struct AboutView: View {
    @ObservedObject var model: AboutModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                Image(nsImage: Bundle.main.image(forResource: "AppIcon") ?? logoImage())
                    .resizable()
                    .interpolation(.none)
                    .frame(width: 45, height: 40)
                VStack(alignment: .leading, spacing: 2) {
                    Text(AppInfo.name).font(.system(size: 17, weight: .semibold))
                    Text(T("Versi ", "Version ") + AppInfo.version).foregroundColor(.secondary)
                }
            }
            Text(T("Memantau sisa batas pemakaian akun Claude (claude CLI) dan ChatGPT (Codex CLI) — 5 jam & 7 hari — serta kuota mingguan Antigravity (agy CLI) dari menu bar.",
                   "Shows the remaining usage limits of your Claude (claude CLI) and ChatGPT (Codex CLI) accounts (5 hours & 7 days) and your weekly Antigravity (agy CLI) quota in the menu bar."))
                .fixedSize(horizontal: false, vertical: true)
            if let url = URL(string: AppInfo.repoURL) {
                Link(AppInfo.repoURL, destination: url)
            }
            Text(T("© 2026 Naraya IT Solution · Lisensi MIT. Tidak berafiliasi dengan Anthropic, OpenAI, maupun Google.",
                   "© 2026 Naraya IT Solution · MIT License. Not affiliated with Anthropic, OpenAI or Google."))
                .font(.system(size: 11)).foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text(model.status)
                .font(.system(size: 11)).foregroundColor(model.statusColor)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button(T("Tutup", "Close")) { model.onClose?() }
                Button(T("Periksa update", "Check for updates")) { model.check() }
                    .disabled(model.checking)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 380)
    }
}

// MARK: - Donors

struct Donor: Identifiable {
    let id = UUID()
    let name: String
    let note: String?
    let date: String?
}

/// Reads donors.json from the repository:
/// { "donate_url": "https://...", "donors": [ { "name": "Budi", "note": "Semangat!", "date": "2026-11-19" } ] }
func fetchDonors() async throws -> (donateURL: String, donors: [Donor]) {
    let root: [String: Any]
    do {
        root = try await httpGet(AppInfo.donorsURL + "?t=\(Int(Date().timeIntervalSince1970))", [:])
    } catch FetchError.http(let code, _) {
        if code == 404 { return (AppInfo.donateURL, []) } // file not published yet
        throw FetchError.message(T("GitHub membalas HTTP ", "GitHub answered HTTP ") + "\(code).")
    } catch {
        throw FetchError.message(T("Koneksi gagal: ", "Connection failed: ") + error.localizedDescription)
    }
    var url = AppInfo.donateURL
    if let u = root["donate_url"] as? String, u.hasPrefix("https://") || u.hasPrefix("http://") { url = u }
    var list: [Donor] = []
    for item in root["donors"] as? [Any] ?? [] {
        if let name = item as? String {
            let n = name.trimmingCharacters(in: .whitespacesAndNewlines)
            if !n.isEmpty { list.append(Donor(name: n, note: nil, date: nil)) }
        } else if let d = item as? [String: Any] {
            let n = (d["name"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            if n.isEmpty { continue }
            list.append(Donor(name: n, note: (d["note"] as? String) ?? (d["message"] as? String), date: d["date"] as? String))
        }
    }
    return (url, list)
}

@MainActor
final class DonorsModel: ObservableObject {
    @Published var loading = true
    @Published var error: String?
    @Published var donors: [Donor] = []
    @Published var donateURL = AppInfo.donateURL
    var onClose: (@MainActor () -> Void)?

    init() {
        Task {
            do {
                let r = try await fetchDonors()
                self.donateURL = r.donateURL
                self.donors = r.donors
            } catch let e {
                self.error = T("Daftar donatur gagal dimuat: ", "Couldn't load the donor list: ") + updateErrorText(e)
            }
            self.loading = false
        }
    }
}

/// Heart button: thanks the supporters listed in donors.json and links to the donation page.
struct DonorsView: View {
    @ObservedObject var model: DonorsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 6) {
                Image(systemName: "heart.fill").foregroundColor(.pink)
                Text(T("Dukung ", "Support ") + AppInfo.name).font(.system(size: 17, weight: .semibold))
            }
            Text(T("\(AppInfo.name) gratis dan open source. Donasi Anda membantu aplikasi ini terus dirawat dan dikembangkan.",
                   "\(AppInfo.name) is free and open source. Your donation helps keep it maintained and improved."))
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text(T("Terima kasih kepada para donatur:", "Thank you to our donors:")).font(.system(size: 13, weight: .semibold))
            ScrollView {
                VStack(alignment: .leading, spacing: 6) {
                    if model.loading {
                        Text(T("Memuat daftar donatur…", "Loading the donor list…")).foregroundColor(.secondary)
                    } else if let e = model.error {
                        Text(e).foregroundColor(.red).fixedSize(horizontal: false, vertical: true)
                    } else if model.donors.isEmpty {
                        Text(T("Belum ada donatur. Jadilah yang pertama!", "No donors yet. Be the first!")).foregroundColor(.secondary)
                    } else {
                        ForEach(model.donors) { d in
                            VStack(alignment: .leading, spacing: 1) {
                                Text("♥ " + d.name + (d.date.map { "  ·  " + $0 } ?? ""))
                                if let n = d.note, !n.isEmpty {
                                    Text(n).font(.system(size: 11)).foregroundColor(.secondary)
                                        .fixedSize(horizontal: false, vertical: true)
                                }
                            }
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
            }
            .frame(height: 170)
            .background(Color.primary.opacity(0.06))
            .cornerRadius(6)
            Text(T("Ingin nama Anda tercantum? Tulis nama di catatan donasi.", "Want your name listed? Add it to the donation note."))
                .font(.system(size: 11)).foregroundColor(.secondary)
            HStack {
                Spacer()
                Button(T("Tutup", "Close")) { model.onClose?() }
                Button(T("Donasi", "Donate")) {
                    if let u = URL(string: model.donateURL) { NSWorkspace.shared.open(u) }
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 380)
    }
}

// MARK: - App delegate

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    var statusItem: NSStatusItem!
    let popover = NSPopover()
    let store = Store()
    var timer: Timer?
    var tick: Timer?
    var alerted: [String: Bool] = [:]
    var canNotify: Bool { Bundle.main.bundleIdentifier != nil }
    var aboutWindow: NSWindow?
    var updateWindow: NSWindow?
    var updateModel: UpdateModel?
    var donorsWindow: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let b = statusItem.button {
            b.image = makeIcon(store.shown.map { _ in nil })
            b.toolTip = T("Usage Monitor - memuat…", "Usage Monitor - loading…")
            b.action = #selector(statusClicked(_:))
            b.target = self
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }
        popover.behavior = .transient
        popover.contentViewController = NSHostingController(
            rootView: PopupView(store: store,
                                onInfo: { [weak self] in self?.showAbout() },
                                onDonors: { [weak self] in self?.showDonors() },
                                onSettings: { [weak self] in self?.showMenu() }))

        store.onUpdate = { [weak self] in self?.didUpdate() }
        if canNotify {
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        }
        timer = Timer.scheduledTimer(withTimeInterval: refreshInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.store.redetect()
                self?.store.refresh()
            }
        }
        tick = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in
            Task { @MainActor in
                if self?.popover.isShown == true { self?.store.now = Date() }
            }
        }
        store.refresh()

        // Check GitHub for a newer version shortly after start up
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 4_000_000_000)
            self.checkUpdateInBackground()
        }
    }

    // MARK: Update & about windows

    func checkUpdateInBackground() {
        Task { @MainActor in
            // offline or GitHub unavailable: try again next start
            guard let r = try? await Updater.fetchLatest(), Updater.isNewer(r), !Updater.isSkipped(r) else { return }
            self.showUpdate(r)
        }
    }

    func makeWindow<V: View>(_ title: String, _ view: V) -> NSWindow {
        let w = NSWindow(contentViewController: NSHostingController(rootView: view))
        w.title = title
        w.styleMask = [.titled, .closable]
        w.level = .floating
        w.isReleasedWhenClosed = false
        w.center()
        return w
    }

    func present(_ w: NSWindow) {
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    func showAbout() {
        if popover.isShown { popover.performClose(nil) }
        if aboutWindow == nil {
            let m = AboutModel()
            m.onFound = { [weak self] r in
                self?.aboutWindow?.close()
                self?.showUpdate(r)
            }
            m.onClose = { [weak self] in self?.aboutWindow?.close() }
            aboutWindow = makeWindow(T("Tentang ", "About ") + AppInfo.name, AboutView(model: m))
        }
        if let w = aboutWindow { present(w) }
    }

    func showDonors() {
        if popover.isShown { popover.performClose(nil) }
        if donorsWindow == nil {
            let m = DonorsModel()
            m.onClose = { [weak self] in self?.donorsWindow?.close() }
            donorsWindow = makeWindow(T("Donatur ", "Donors of ") + AppInfo.name, DonorsView(model: m))
        }
        if let w = donorsWindow { present(w) }
    }

    func showUpdate(_ r: ReleaseInfo, force: Bool = false) {
        if !force, let m = updateModel, let w = updateWindow, w.isVisible, Updater.compare(m.release.version, r.version) == 0 {
            present(w)
            return
        }
        updateWindow?.close()
        let m = UpdateModel(r)
        m.onClose = { [weak self] in self?.updateWindow?.close() }
        m.onQuit = { NSApp.terminate(nil) }
        updateModel = m
        let w = makeWindow("Update " + AppInfo.name, UpdateView(model: m))
        updateWindow = w
        present(w)
    }

    /// Left click = popover, right click (or Control+click) = menu.
    @objc func statusClicked(_ sender: Any?) {
        let event = NSApp.currentEvent
        if event?.type == .rightMouseUp || event?.modifierFlags.contains(.control) == true {
            showMenu()
        } else {
            togglePopover(sender)
        }
    }

    func showMenu() {
        guard let button = statusItem.button else { return }
        if popover.isShown { popover.performClose(nil) }
        store.redetect()

        let menu = NSMenu()
        menu.autoenablesItems = false
        menu.addItem(menuItem(T("Tampilkan usage", "Show usage"), #selector(menuShow)))
        menu.addItem(menuItem(T("Refresh sekarang", "Refresh now"), #selector(menuRefresh)))
        menu.addItem(.separator())

        // Provider selection: only CLIs installed on this Mac are listed
        let header = NSMenuItem(title: T("Tampilkan akun:", "Show accounts:"), action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)
        let shown = store.shown
        for p in store.installed {
            let item = menuItem(p.menuTitle, #selector(menuToggleProvider(_:)))
            item.state = shown.contains(p) ? .on : .off
            item.representedObject = p.id
            menu.addItem(item)
        }
        menu.addItem(.separator())

        let login = menuItem(T("Jalankan saat login", "Run at login"), #selector(menuToggleLogin))
        login.state = store.loginEnabled ? .on : .off
        menu.addItem(login)

        // Language: follow macOS, or a fixed language
        let langItem = NSMenuItem(title: T("Bahasa", "Language"), action: nil, keyEquivalent: "")
        let langMenu = NSMenu()
        langMenu.autoenablesItems = false
        let systemName = Lang.systemIsIndonesian ? "Bahasa Indonesia" : "English"
        for (code, name) in [("", T("Ikuti sistem", "Follow system") + " (\(systemName))"),
                             ("id", "Bahasa Indonesia"), ("en", "English")] {
            let item = menuItem(name, #selector(menuLanguage(_:)))
            item.state = Lang.choice == code ? .on : .off
            item.representedObject = code
            langMenu.addItem(item)
        }
        langItem.submenu = langMenu
        menu.addItem(langItem)
        menu.addItem(.separator())

        menu.addItem(menuItem(T("Donatur & donasi…", "Donors & donate…"), #selector(menuDonors)))
        menu.addItem(menuItem(T("Tentang & update…", "About & update…"), #selector(menuAbout)))
        menu.addItem(.separator())
        menu.addItem(menuItem(T("Keluar", "Quit"), #selector(menuQuit)))

        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: button.bounds.height + 5), in: button)
    }

    func menuItem(_ title: String, _ action: Selector) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        return item
    }

    @objc func menuShow() { togglePopover(nil) }
    @objc func menuRefresh() { store.refresh() }
    @objc func menuToggleLogin() { store.setLogin(!store.loginEnabled) }
    @objc func menuQuit() { NSApp.terminate(nil) }
    @objc func menuAbout() { showAbout() }
    @objc func menuDonors() { showDonors() }
    @objc func menuLanguage(_ sender: NSMenuItem) {
        guard let code = sender.representedObject as? String, code != Lang.choice else { return }
        let wasID = Lang.isID
        store.setLanguage(code)
        guard Lang.isID != wasID else { return }
        // Reopen windows in the new language
        for w in [aboutWindow, donorsWindow] { w?.close() }
        aboutWindow = nil
        donorsWindow = nil
        if let m = updateModel, let w = updateWindow, w.isVisible {
            if case .downloading = m.phase { return }
            showUpdate(m.release, force: true)
        }
    }
    @objc func menuToggleProvider(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String,
              let p = store.installed.first(where: { $0.id == id }) else { return }
        store.toggle(p)
    }

    @objc func togglePopover(_ sender: Any?) {
        guard let button = statusItem.button else { return }
        if popover.isShown {
            popover.performClose(sender)
            return
        }
        store.now = Date()
        NSApp.activate(ignoringOtherApps: true)
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
        let stale = store.updated.map { Date().timeIntervalSince($0) > 60 } ?? true
        if !store.busy && stale { store.refresh() }
    }

    func didUpdate() {
        let shown = store.shown
        statusItem.button?.image = makeIcon(shown.map { store.data[$0.id] })
        statusItem.button?.toolTip = shown.map { tipLine($0.title, store.data[$0.id]) }.joined(separator: "\n")
        for def in shown {
            guard let p = store.data[def.id], p.error == nil else { continue }
            for (i, w) in p.rows.enumerated() { checkAlert(p, i, w) }
        }
    }

    /// Keyed by row position, not label, so switching the language doesn't repeat an alert.
    func checkAlert(_ p: ProviderStatus, _ index: Int, _ w: UsageWindow) {
        let key = "\(p.name)#\(index)"
        let label = w.label
        let high = (w.percent ?? 0) >= 90
        if high && !(alerted[key] ?? false) && canNotify {
            let c = UNMutableNotificationContent()
            c.title = T("Usage \(p.name) tinggi", "\(p.name) usage is high")
            c.body = "\(p.name) \(label): \(pctText(w.percent))" + T(" terpakai. ", " used. ") + resetText(w.resetAt)
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: c, trigger: nil))
        }
        alerted[key] = high
    }
}

// MARK: - Entry point

@main
struct UsageMonitorMain {
    @MainActor
    static func main() {
        if CommandLine.arguments.contains("--test") {
            Task {
                let inst = allProviders.filter { $0.installed() }.map { $0.id }
                print("installed: " + (inst.isEmpty ? "(none)" : inst.joined(separator: ",")))
                for p in [await fetchClaude(), await fetchCodex(), await fetchAntigravity()] {
                    print("\(p.name) plan=\(p.plan ?? "-") account=\(p.account ?? "-") error=\(p.error ?? "-")")
                    for w in p.rows { print("  \(w.label): \(pctText(w.percent)) | \(shortReset(w.resetAt))") }
                    for info in p.infos { print("  " + info) }
                }
                exit(0)
            }
            RunLoop.main.run()
            return
        }
        migrateFromUsageTray()
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory) // menu bar only, no Dock icon
        withExtendedLifetime(delegate) { app.run() }
    }
}
