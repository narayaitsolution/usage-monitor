// UsageTray (macOS) - menu bar monitor for Claude (claude CLI) and ChatGPT (Codex CLI) usage limits.
// Build with ./build.sh (requires Xcode Command Line Tools, macOS 12+).
import Cocoa
import SwiftUI
import UserNotifications

// MARK: - Model

struct UsageWindow {
    var percent: Double?
    var resetAt: Date?
}

struct ProviderStatus {
    var name: String
    var plan: String?
    var account: String?
    var error: String?
    var five = UsageWindow()
    var week = UsageWindow()
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
let homeDir = FileManager.default.homeDirectoryForCurrentUser

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

func httpGet(_ url: String, _ headers: [String: String]) async throws -> [String: Any] {
    var req = URLRequest(url: URL(string: url)!)
    req.timeoutInterval = 20
    req.setValue("application/json", forHTTPHeaderField: "Accept")
    req.setValue("UsageTray/1.0", forHTTPHeaderField: "User-Agent")
    for (k, v) in headers { req.setValue(v, forHTTPHeaderField: k) }
    let (data, resp) = try await URLSession.shared.data(for: req)
    let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
    guard code == 200 else {
        throw FetchError.http(code, String(data: data.prefix(120), encoding: .utf8) ?? "")
    }
    guard let obj = parseJSON(data) else { throw FetchError.message("Respons bukan JSON") }
    return obj
}

func errorText(_ error: Error, cli: String) -> String {
    if case let FetchError.http(code, body) = error {
        if code == 429 { return "Dibatasi server (429). Dicoba lagi otomatis." }
        if code == 401 || code == 403 { return "Token ditolak (\(code)). Buka '\(cli)' agar token di-refresh." }
        return "HTTP \(code): \(body)"
    }
    if case let FetchError.message(m) = error { return m }
    return "Koneksi gagal: \(error.localizedDescription)"
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

func fetchClaude() async -> ProviderStatus {
    var s = ProviderStatus(name: "Claude")
    guard let creds = claudeCredentials(), let oauth = creds["claudeAiOauth"] as? [String: Any] else {
        s.error = "Belum login. Jalankan 'claude' lalu /login."
        return s
    }
    s.plan = oauth["subscriptionType"] as? String
    guard let token = oauth["accessToken"] as? String, !token.isEmpty else {
        s.error = "Token tidak ditemukan. Login ulang via claude CLI."
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
        s.error = "Token kedaluwarsa. Buka 'claude' sekali agar token di-refresh."
        return s
    }
    do {
        let root = try await httpGet("https://api.anthropic.com/api/oauth/usage",
                                     ["Authorization": "Bearer \(token)", "anthropic-beta": "oauth-2025-04-20"])
        for (key, isFive) in [("five_hour", true), ("seven_day", false)] {
            guard let o = root[key] as? [String: Any] else { continue }
            var w = UsageWindow()
            w.percent = num(o["utilization"])
            if let r = o["resets_at"] as? String { w.resetAt = parseISODate(r) }
            if isFive { s.five = w } else { s.week = w }
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
        s.error = "Belum login. Jalankan 'codex login'."
        return s
    }
    let tokens = auth["tokens"] as? [String: Any] ?? [:]
    guard let token = tokens["access_token"] as? String, !token.isEmpty else {
        s.error = "Codex tidak login dengan akun ChatGPT (mode API key?)."
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
            var w = UsageWindow()
            w.percent = num(o["used_percent"])
            if let r = num(o["reset_at"]) { w.resetAt = Date(timeIntervalSince1970: r) }
            let isFive: Bool
            if let secs = num(o["limit_window_seconds"]) { isFive = secs <= 86400 } else { isFive = key == "primary_window" }
            if isFive { s.five = w } else { s.week = w }
        }
    } catch {
        s.error = errorText(error, cli: "codex")
        if case FetchError.http(429, _) = error { s.rateLimited = true }
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

func resetText(_ date: Date?, now: Date = Date()) -> String {
    guard let date = date else { return "" }
    let secs = date.timeIntervalSince(now)
    if secs <= 0 { return "Sudah reset - refresh untuk update" }
    let total = Int(secs)
    let days = total / 86400, hours = (total % 86400) / 3600, mins = (total % 3600) / 60
    let d: String
    if days >= 1 { d = "\(days) hari \(hours) jam" }
    else if hours >= 1 { d = "\(hours) jam \(mins) mnt" }
    else { d = "\(max(1, Int((secs / 60).rounded(.up)))) mnt" }
    let r = date.addingTimeInterval(30)
    let f = DateFormatter()
    f.locale = Locale(identifier: "id_ID")
    f.dateFormat = Calendar.current.isDate(r, inSameDayAs: now) ? "HH:mm" : "EEE d MMM, HH:mm"
    return "Reset dalam \(d)  ·  \(f.string(from: r))"
}

// MARK: - Login item (LaunchAgent)

enum LoginItem {
    static let label = "com.usagetray.app"
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
                prev.note = "Dibatasi server (429) · data \(prev.fetchedAt.map { f.string(from: $0) } ?? "-") · coba lagi \(f.string(from: retry))"
                data[id] = prev
                return
            }
            r.error = "Dibatasi server (429). Dicoba lagi \(f.string(from: retry))."
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
}

// MARK: - Views

struct UsageRow: View {
    let label: String
    let window: UsageWindow
    let now: Date

    var body: some View {
        let pct = window.percent
        let color = pct.map(levelColor) ?? Color.gray
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(label).font(.system(size: 12)).foregroundColor(.secondary)
                Spacer()
                Text(pctText(pct))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor((pct ?? 0) >= 50 ? color : .primary)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.gray.opacity(0.3))
                    if let p = pct, p > 0 {
                        Capsule().fill(color)
                            .frame(width: max(8, geo.size.width * CGFloat(min(100, p)) / 100))
                    }
                }
            }
            .frame(height: 8)
            Text(resetText(window.resetAt, now: now)).font(.system(size: 11)).foregroundColor(.secondary)
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
        VStack(alignment: .leading, spacing: 10) {
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
                    UsageRow(label: "5 jam", window: s.five, now: now)
                    UsageRow(label: "7 hari", window: s.week, now: now)
                    if let note = s.note {
                        Text("⚠ " + note).font(.system(size: 11)).foregroundColor(.orange)
                    }
                }
            } else {
                Text(busy ? "Memuat…" : "Belum ada data").font(.system(size: 12)).foregroundColor(.secondary)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.primary.opacity(0.06))
        .cornerRadius(8)
    }
}

struct PopupView: View {
    @ObservedObject var store: Store

    var footer: String {
        guard let u = store.updated else { return "Belum ada data" }
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return "Diperbarui \(f.string(from: u))  ·  otomatis tiap 5 menit"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Usage Monitor").font(.system(size: 14, weight: .semibold))
                Spacer()
                Button(store.busy ? "Memuat…" : "↻ Refresh") { store.refresh() }
                    .disabled(store.busy)
            }
            ForEach(store.shown) { p in
                ProviderCard(title: p.title, accent: p.accent, status: store.data[p.id], busy: store.busy, now: store.now)
            }
            Text(footer).font(.system(size: 11)).foregroundColor(.secondary)
            Divider()
            HStack {
                Toggle("Jalankan saat login", isOn: Binding(get: { store.loginEnabled },
                                                           set: { store.setLogin($0) }))
                    .toggleStyle(.checkbox)
                    .font(.system(size: 12))
                Spacer()
                Button("Keluar") { NSApp.terminate(nil) }
            }
        }
        .padding(14)
        .frame(width: 340)
    }
}

// MARK: - Menu bar icon

/// Two bars (5h, 7d) per shown provider, stacked in menu order.
func makeIcon(_ statuses: [ProviderStatus?]) -> NSImage {
    let width: CGFloat = 18, height: CGFloat = 16
    let n = CGFloat(max(1, statuses.count))
    let gap: CGFloat = 1, groupGap: CGFloat = 2
    let fixed = n * gap + (n - 1) * groupGap
    let barH = max(1, min(7, floor((height - fixed) / (2 * n))))
    let img = NSImage(size: NSSize(width: width, height: height), flipped: true) { _ in
        var y = max(0, floor((height - (2 * n * barH + fixed)) / 2))
        for p in statuses {
            let err = p?.error != nil
            for pct in [p?.five.percent, p?.week.percent] {
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
    guard let p = p else { return "\(title): memuat…" }
    if p.error != nil { return "\(title): error" }
    return "\(title) 5h \(pctText(p.five.percent)) | 7d \(pctText(p.week.percent))"
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

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let b = statusItem.button {
            b.image = makeIcon(store.shown.map { _ in nil })
            b.toolTip = "Usage Monitor - memuat…"
            b.action = #selector(statusClicked(_:))
            b.target = self
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }
        popover.behavior = .transient
        popover.contentViewController = NSHostingController(rootView: PopupView(store: store))

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
        menu.addItem(menuItem("Tampilkan usage", #selector(menuShow)))
        menu.addItem(menuItem("Refresh sekarang", #selector(menuRefresh)))
        menu.addItem(.separator())

        // Provider selection: only CLIs installed on this Mac are listed
        let header = NSMenuItem(title: "Tampilkan akun:", action: nil, keyEquivalent: "")
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

        let login = menuItem("Jalankan saat login", #selector(menuToggleLogin))
        login.state = store.loginEnabled ? .on : .off
        menu.addItem(login)
        menu.addItem(.separator())
        menu.addItem(menuItem("Keluar", #selector(menuQuit)))

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
            checkAlert(p, "5 jam", p.five)
            checkAlert(p, "7 hari", p.week)
        }
    }

    func checkAlert(_ p: ProviderStatus, _ label: String, _ w: UsageWindow) {
        let key = p.name + label
        let high = (w.percent ?? 0) >= 90
        if high && !(alerted[key] ?? false) && canNotify {
            let c = UNMutableNotificationContent()
            c.title = "Usage \(p.name) tinggi"
            c.body = "\(p.name) \(label): \(pctText(w.percent)) terpakai. \(resetText(w.resetAt))"
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: c, trigger: nil))
        }
        alerted[key] = high
    }
}

// MARK: - Entry point

@main
struct UsageTrayMain {
    @MainActor
    static func main() {
        if CommandLine.arguments.contains("--test") {
            Task {
                let inst = allProviders.filter { $0.installed() }.map { $0.id }
                print("installed: " + (inst.isEmpty ? "(none)" : inst.joined(separator: ",")))
                for p in [await fetchClaude(), await fetchCodex()] {
                    print("\(p.name) plan=\(p.plan ?? "-") account=\(p.account ?? "-") error=\(p.error ?? "-")")
                    print("  5h: \(pctText(p.five.percent)) | \(resetText(p.five.resetAt))")
                    print("  7d: \(pctText(p.week.percent)) | \(resetText(p.week.resetAt))")
                }
                exit(0)
            }
            RunLoop.main.run()
            return
        }
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory) // menu bar only, no Dock icon
        withExtendedLifetime(delegate) { app.run() }
    }
}
