import SwiftUI
import AppKit
import WebKit
import Metal
import Combine
import Darwin

// My System for macOS (Apple Silicon). All metrics are measured locally.
// Apple's public APIs do not expose unrestricted GPU load, per-game FPS or chip
// temperatures; unavailable values are never fabricated.
struct Reading: Identifiable {
    let id: String
    let title: String
    let value: Double?
    let unit: String
    let explanation: String
    var text: String {
        guard let v = value, v.isFinite else { return "N/A" }
        if unit == "%" || unit == "°C" { return String(format: "%.1f%@", v, unit) }
        if unit == "GB" { return String(format: "%.2f GB", v) }
        if unit == "MB/s" { return String(format: "%.2f MB/s", v) }
        return String(format: "%.1f %@", v, unit)
    }
    var source: String { value == nil ? "Not available through supported macOS APIs" : explanation }
}

struct SystemSnapshot {
    var cpu: Double?
    var usedGB: Double?
    var totalGB: Double = Double(ProcessInfo.processInfo.physicalMemory) / 1_073_741_824
    var diskFreeGB: Double?
    var diskTotalGB: Double?
    var downMBs: Double?
    var upMBs: Double?
    var gpuName: String = MTLCreateSystemDefaultDevice()?.name ?? "GPU information unavailable"
    var model: String = SystemProbe.sysctlString("hw.model") ?? "Apple Silicon Mac"
    var logicalCores: Int = ProcessInfo.processInfo.activeProcessorCount
    var timestamp = Date()
    var readings: [Reading] {
        let memoryPct = usedGB.map { $0 / max(1, totalGB) * 100 }
        let diskPct = (diskFreeGB != nil && diskTotalGB != nil) ? (1 - diskFreeGB! / max(1, diskTotalGB!)) * 100 : nil
        return [
            Reading(id: "cpu.usage", title: "CPU Usage", value: cpu, unit: "%", explanation: "Mach host CPU ticks"),
            Reading(id: "gpu.usage", title: "GPU Load", value: nil, unit: "%", explanation: "Not exposed by stable public macOS API"),
            Reading(id: "memory.usage", title: "Memory Used", value: memoryPct, unit: "%", explanation: "Mach active + wired + compressed estimate"),
            Reading(id: "memory.used", title: "Physical Memory", value: usedGB, unit: "GB", explanation: "Mach VM statistics; approximate"),
            Reading(id: "memory.total", title: "Unified Memory", value: totalGB, unit: "GB", explanation: "ProcessInfo physicalMemory"),
            Reading(id: "cpu.temperature", title: "CPU Temperature", value: nil, unit: "°C", explanation: "No public Apple Silicon CPU temperature API"),
            Reading(id: "gpu.temperature", title: "GPU Temperature", value: nil, unit: "°C", explanation: "No public Apple Silicon GPU temperature API"),
            Reading(id: "disk.activity", title: "Storage Used", value: diskPct, unit: "%", explanation: "Filesystem capacity (not physical disk activity)"),
            Reading(id: "disk.free", title: "Storage Available", value: diskFreeGB, unit: "GB", explanation: "Filesystem available capacity"),
            Reading(id: "disk.total", title: "Storage Capacity", value: diskTotalGB, unit: "GB", explanation: "Filesystem capacity"),
            Reading(id: "network.download", title: "Download", value: downMBs, unit: "MB/s", explanation: "BSD network interface byte counters"),
            Reading(id: "network.upload", title: "Upload", value: upMBs, unit: "MB/s", explanation: "BSD network interface byte counters"),
            Reading(id: "gpu.fps", title: "Game FPS", value: nil, unit: "fps", explanation: "Per-game FPS capture is not supported in this preview")
        ]
    }
}

enum SystemProbe {
    static func sysctlString(_ key: String) -> String? {
        var length: size_t = 0
        guard sysctlbyname(key, nil, &length, nil, 0) == 0 && length > 1 else { return nil }
        var chars = [CChar](repeating: 0, count: length)
        guard sysctlbyname(key, &chars, &length, nil, 0) == 0 else { return nil }
        return chars.withUnsafeBufferPointer { String(cString: $0.baseAddress!) }
    }

    static func cpuTicks() -> [UInt64]? {
        var info = host_cpu_load_info()
        var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.size / MemoryLayout<integer_t>.stride)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { return nil }
        return [UInt64(info.cpu_ticks.0), UInt64(info.cpu_ticks.1),
                UInt64(info.cpu_ticks.2), UInt64(info.cpu_ticks.3)]
    }

    static func memoryUsedGB() -> Double? {
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size / MemoryLayout<integer_t>.stride)
        let result = withUnsafeMutablePointer(to: &stats) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { return nil }
        let pages = UInt64(stats.active_count) + UInt64(stats.wire_count) + UInt64(stats.compressor_page_count)
        return Double(pages) * Double(vm_kernel_page_size) / 1_073_741_824
    }

    static func disk() -> (Double?, Double?) {
        guard let attributes = try? FileManager.default.attributesOfFileSystem(forPath: NSHomeDirectory()),
              let total = attributes[.systemSize] as? NSNumber,
              let free = attributes[.systemFreeSize] as? NSNumber else { return (nil, nil) }
        return (free.doubleValue / 1_073_741_824, total.doubleValue / 1_073_741_824)
    }

    static func networkBytes() -> (UInt64, UInt64)? {
        var list: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&list) == 0, let first = list else { return nil }
        defer { freeifaddrs(first) }
        var incoming: UInt64 = 0
        var outgoing: UInt64 = 0
        var cursor: UnsafeMutablePointer<ifaddrs>? = first
        while let ptr = cursor {
            let item = ptr.pointee
            let name = String(cString: item.ifa_name)
            // Exclude loopback, virtual tunnels, bridges and Apple peer-to-peer.
            if name.hasPrefix("en"),
               (item.ifa_flags & UInt32(IFF_UP)) != 0,
               let address = item.ifa_addr,
               address.pointee.sa_family == UInt8(AF_LINK),
               let data = item.ifa_data {
                let counters = data.assumingMemoryBound(to: if_data.self).pointee
                incoming += UInt64(counters.ifi_ibytes)
                outgoing += UInt64(counters.ifi_obytes)
            }
            cursor = item.ifa_next
        }
        return (incoming, outgoing)
    }
}

@MainActor
final class SystemMonitor: ObservableObject {
    @Published var snapshot = SystemSnapshot()
    @Published var cpuHistory = [Double]()
    @Published var memoryHistory = [Double]()
    private var timer: Timer?
    private var previousTicks: [UInt64]?
    private var previousNetwork: (UInt64, UInt64, Date)?
    init() {
        sample()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.sample() }
        }
        timer?.tolerance = 0.15
    }
    private func sample() {
        var next = snapshot
        next.timestamp = Date()
        if let ticks = SystemProbe.cpuTicks() {
            if let previous = previousTicks, ticks.count == previous.count {
                let delta = zip(ticks, previous).map { $0 >= $1 ? $0 - $1 : 0 }
                let total = delta.reduce(0, +)
                next.cpu = total > 0 ? min(100, max(0, Double(total - delta[2]) * 100 / Double(total))) : nil
            }
            previousTicks = ticks
        } else { next.cpu = nil }
        next.usedGB = SystemProbe.memoryUsedGB()
        let (free, total) = SystemProbe.disk()
        next.diskFreeGB = free
        next.diskTotalGB = total
        if let bytes = SystemProbe.networkBytes() {
            if let prior = previousNetwork {
                let seconds = Date().timeIntervalSince(prior.2)
                if seconds > 0 && bytes.0 >= prior.0 && bytes.1 >= prior.1 {
                    next.downMBs = Double(bytes.0 - prior.0) / 1_000_000 / seconds
                    next.upMBs = Double(bytes.1 - prior.1) / 1_000_000 / seconds
                } else { next.downMBs = nil; next.upMBs = nil }
            }
            previousNetwork = (bytes.0, bytes.1, Date())
        } else { next.downMBs = nil; next.upMBs = nil }
        snapshot = next
        if let cpu = next.cpu { cpuHistory.append(cpu) }
        if let used = next.usedGB { memoryHistory.append(used / max(1, next.totalGB) * 100) }
        if cpuHistory.count > 90 { cpuHistory.removeFirst(cpuHistory.count - 90) }
        if memoryHistory.count > 90 { memoryHistory.removeFirst(memoryHistory.count - 90) }
    }
    deinit { timer?.invalidate() }
}

enum Section: String, CaseIterable, Identifiable {
    case overview = "Overview", cpu = "CPU", gpu = "GPU", memory = "Memory", thermal = "Thermals"
    case gaming = "Gaming", disk = "Storage", network = "Network", system = "System Info"
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .overview: return "square.grid.2x2.fill"
        case .cpu: return "cpu.fill"
        case .gpu: return "square.stack.3d.up.fill"
        case .memory: return "memorychip.fill"
        case .thermal: return "thermometer.medium"
        case .gaming: return "gamecontroller.fill"
        case .disk: return "internaldrive.fill"
        case .network: return "network"
        case .system: return "info.circle.fill"
        }
    }
}

private let canvas = Color(red: 0.065, green: 0.090, blue: 0.111)
private let surface = Color(red: 0.107, green: 0.137, blue: 0.159)
private let teal = Color(red: 0.31, green: 0.85, blue: 0.75)
private let amber = Color(red: 0.94, green: 0.68, blue: 0.39)

@main
struct MySystemMacApp: App {
    var body: some Scene {
        WindowGroup("My System") { Dashboard().preferredColorScheme(.dark) }
            .defaultSize(width: 1330, height: 870)
    }
}

struct Dashboard: View {
    @StateObject private var monitor = SystemMonitor()
    @State private var selection: Section = .overview
    var body: some View {
        HStack(spacing: 0) {
            sidebar.frame(width: 208)
            Rectangle().fill(.white.opacity(0.08)).frame(width: 1)
            ScrollView {
                VStack(alignment: .leading, spacing: 23) {
                    header
                    if selection == .overview {
                        HStack(alignment: .top, spacing: 17) {
                            atlas.frame(minWidth: 520, minHeight: 445)
                            VStack(spacing: 14) {
                                MetricCard(title: "PROCESSOR", reading: metric("cpu.usage"), symbol: "cpu", history: monitor.cpuHistory, tint: teal)
                                MetricCard(title: "UNIFIED MEMORY", reading: metric("memory.usage"), symbol: "memorychip", history: monitor.memoryHistory, tint: amber)
                                MetricCard(title: "GRAPHICS", reading: metric("gpu.usage"), symbol: "square.stack.3d.up", subtitle: monitor.snapshot.gpuName, history: [], tint: teal)
                            }.frame(maxWidth: 315)
                        }
                        HStack(spacing: 14) {
                            compact("STORAGE", metric("disk.free"))
                            compact("DOWNLOAD", metric("network.download"))
                            compact("UPLOAD", metric("network.upload"))
                        }
                    } else if selection == .system {
                        infoPanel
                    } else {
                        detail
                    }
                    Text("LOCAL TELEMETRY  •  NO ACCOUNT  •  NO CLOUD UPLOAD  •  UNSUPPORTED METRICS = N/A")
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundStyle(.white.opacity(0.37))
                        .padding(.top, 6)
                }
                .padding(27)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .background(canvas)
        }
        .background(canvas)
        .frame(minWidth: 960, minHeight: 650)
    }
    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 24) {
            VStack(alignment: .leading, spacing: 6) {
                Text("MY SYSTEM").font(.system(size: 21, weight: .heavy, design: .rounded)).tracking(1.2)
                Text("APPLE SILICON / LIVE").font(.system(size: 9, weight: .semibold, design: .monospaced))
                    .tracking(1.25).foregroundStyle(teal)
            }.padding(.top, 30).padding(.horizontal, 18)
            VStack(spacing: 4) {
                ForEach(Section.allCases) { item in
                    Button { selection = item } label: {
                        HStack(spacing: 13) {
                            Image(systemName: item.symbol).frame(width: 21)
                            Text(item.rawValue).font(.system(size: 13, weight: .medium))
                            Spacer()
                        }
                        .padding(.horizontal, 15).padding(.vertical, 12)
                        .background(selection == item ? teal.opacity(0.14) : .clear)
                        .foregroundStyle(selection == item ? teal : .white.opacity(0.72))
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                    }.buttonStyle(.plain)
                }
            }.padding(.horizontal, 9)
            Spacer()
            VStack(alignment: .leading, spacing: 7) {
                Label("LOCAL MONITORING", systemImage: "checkmark.shield.fill")
                    .font(.system(size: 10, weight: .bold, design: .monospaced)).foregroundStyle(teal)
                Text(monitor.snapshot.model).font(.system(size: 11)).foregroundStyle(.white.opacity(0.55))
                Text("macOS • ARM64").font(.system(size: 10, design: .monospaced)).foregroundStyle(.white.opacity(0.4))
            }.padding(18)
        }.frame(maxHeight: .infinity).background(surface.opacity(0.68))
    }
    private var header: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 8) {
                Text(selection == .overview ? "System command center" : selection.rawValue)
                    .font(.system(size: 29, weight: .bold, design: .rounded))
                Text("My System for macOS  •  Native Apple Silicon monitoring")
                    .font(.system(size: 12)).foregroundStyle(.white.opacity(0.56))
            }
            Spacer()
            HStack(spacing: 7) {
                Circle().fill(teal).frame(width: 7, height: 7)
                Text("LIVE")
                Text(monitor.snapshot.timestamp.formatted(date: .omitted, time: .standard)).foregroundStyle(.white.opacity(0.6))
            }.font(.system(size: 11, design: .monospaced))
                .padding(.horizontal, 13).padding(.vertical, 10)
                .background(surface).clipShape(Capsule())
        }
    }
    private var atlas: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Label("3D HARDWARE ATLAS", systemImage: "cube.transparent")
                    .font(.system(size: 11, weight: .bold, design: .monospaced)).foregroundStyle(teal)
                Spacer()
                Text("ILLUSTRATIVE MODEL").font(.system(size: 9, design: .monospaced)).foregroundStyle(.white.opacity(0.44))
            }.padding(15)
            HardwareAtlas(snapshot: monitor.snapshot) { component in
                switch component {
                case "cpu": selection = .cpu
                case "gpu": selection = .gpu
                case "memory": selection = .memory
                case "storage": selection = .disk
                case "cooling": selection = .thermal
                default: selection = .overview
                }
            }.frame(minHeight: 405)
        }
        .background(surface).clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(.white.opacity(0.07)))
    }
    private func metric(_ id: String) -> Reading {
        monitor.snapshot.readings.first { $0.id == id } ??
            Reading(id: id, title: id, value: nil, unit: "", explanation: "Unavailable")
    }
    private func compact(_ title: String, _ reading: Reading) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.system(size: 10, weight: .bold, design: .monospaced)).foregroundStyle(.white.opacity(0.55))
            Text(reading.text).font(.system(size: 23, weight: .semibold, design: .rounded))
            Text(reading.source).font(.system(size: 10)).foregroundStyle(.white.opacity(0.42)).lineLimit(2)
        }.padding(17).frame(maxWidth: .infinity, alignment: .leading)
            .background(surface).clipShape(RoundedRectangle(cornerRadius: 14))
    }
    private var detail: some View {
        VStack(alignment: .leading, spacing: 15) {
            if selection == .cpu || selection == .gpu || selection == .memory || selection == .disk || selection == .thermal {
                atlas.frame(height: 470)
            }
            if selection == .gaming {
                notice("Game FPS capture", "Apple Silicon macOS does not provide a general public API for capturing arbitrary games' FPS. This preview does not install graphics hooks or invent measurements.")
            } else if selection == .thermal {
                notice("Temperature sensor availability", "Apple Silicon CPU/GPU temperatures are not accessible through supported unprivileged macOS APIs. Temperature indicators show N/A instead of fabricated values.")
            } else if selection == .gpu {
                notice("GPU activity support", "Detected: " + monitor.snapshot.gpuName + ". GPU utilization and temperature require unsupported or privileged mechanisms and are therefore N/A.")
            }
            let keys: [String] = {
                switch selection {
                case .cpu: return ["cpu.usage", "cpu.temperature"]
                case .gpu: return ["gpu.usage", "gpu.temperature"]
                case .memory: return ["memory.usage", "memory.used", "memory.total"]
                case .thermal: return ["cpu.temperature", "gpu.temperature"]
                case .gaming: return ["gpu.fps", "gpu.usage"]
                case .disk: return ["disk.activity", "disk.free", "disk.total"]
                case .network: return ["network.download", "network.upload"]
                default: return ["cpu.usage", "memory.usage"]
                }
            }()
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 15)], spacing: 15) {
                ForEach(keys, id: \.self) { key in
                    let r = metric(key)
                    VStack(alignment: .leading, spacing: 12) {
                        Text(r.title.uppercased()).font(.system(size: 10, weight: .bold, design: .monospaced)).foregroundStyle(teal)
                        Text(r.text).font(.system(size: 28, weight: .bold, design: .rounded))
                        Text(r.source).font(.system(size: 11)).foregroundStyle(.white.opacity(0.50))
                    }.frame(maxWidth: .infinity, minHeight: 115, alignment: .leading).padding(20)
                        .background(surface).clipShape(RoundedRectangle(cornerRadius: 13))
                }
            }
            if selection == .cpu || selection == .memory {
                VStack(alignment: .leading, spacing: 13) {
                    Text("LAST 90 LIVE SAMPLES").font(.system(size: 10, weight: .bold, design: .monospaced)).foregroundStyle(teal)
                    Sparkline(values: selection == .cpu ? monitor.cpuHistory : monitor.memoryHistory, tint: teal)
                        .frame(height: 100)
                }.padding(20).background(surface).clipShape(RoundedRectangle(cornerRadius: 14))
            }
        }
    }
    private var infoPanel: some View {
        VStack(alignment: .leading, spacing: 18) {
            infoRow("Computer", monitor.snapshot.model)
            infoRow("GPU", monitor.snapshot.gpuName)
            infoRow("Logical CPU cores", String(monitor.snapshot.logicalCores))
            infoRow("Unified memory", String(format: "%.2f GB", monitor.snapshot.totalGB))
            infoRow("Architecture", "Apple Silicon / arm64")
            infoRow("System", ProcessInfo.processInfo.operatingSystemVersionString)
            infoRow("Data policy", "All monitoring is local. No telemetry is uploaded.")
            notice("Preview limitations", "The 3D atlas depicts a representative PC assembly, not the internal layout of your MacBook Pro. GPU FPS and silicon temperature are intentionally unavailable.")
        }.padding(22).background(surface).clipShape(RoundedRectangle(cornerRadius: 16))
    }
    private func infoRow(_ key: String, _ value: String) -> some View {
        HStack(alignment: .top) {
            Text(key).foregroundStyle(.white.opacity(0.55)).frame(width: 190, alignment: .leading)
            Text(value).textSelection(.enabled)
            Spacer(minLength: 0)
        }.font(.system(size: 13))
    }
    private func notice(_ title: String, _ message: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "info.circle").foregroundStyle(amber)
            VStack(alignment: .leading, spacing: 5) {
                Text(title).font(.system(size: 13, weight: .bold))
                Text(message).font(.system(size: 12)).foregroundStyle(.white.opacity(0.6))
            }
        }.padding(16).frame(maxWidth: .infinity, alignment: .leading)
            .background(amber.opacity(0.08)).clipShape(RoundedRectangle(cornerRadius: 13))
    }
}

struct MetricCard: View {
    let title: String
    let reading: Reading
    let symbol: String
    var subtitle: String = ""
    let history: [Double]
    let tint: Color
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(title).font(.system(size: 10, weight: .bold, design: .monospaced)).tracking(1)
                Spacer()
                Image(systemName: symbol)
            }.foregroundStyle(tint)
            Text(reading.text).font(.system(size: 29, weight: .bold, design: .rounded))
                .minimumScaleFactor(0.65).lineLimit(1)
            if !subtitle.isEmpty {
                Text(subtitle).font(.system(size: 11)).lineLimit(1).foregroundStyle(.white.opacity(0.55))
            } else {
                Text(reading.source).font(.system(size: 10)).lineLimit(2).foregroundStyle(.white.opacity(0.52))
            }
            Sparkline(values: history, tint: tint).frame(height: 34)
        }.padding(17).frame(maxWidth: .infinity, alignment: .leading)
            .background(surface).clipShape(RoundedRectangle(cornerRadius: 14))
    }
}

struct Sparkline: View {
    let values: [Double]
    let tint: Color
    var body: some View {
        GeometryReader { size in
            ZStack {
                Path { path in
                    path.move(to: CGPoint(x: 0, y: size.height))
                    path.addLine(to: CGPoint(x: size.width, y: size.height))
                }.stroke(.white.opacity(0.10), lineWidth: 1)
                if values.count > 1 {
                    Path { path in
                        for (index, raw) in values.enumerated() {
                            let y = size.height * (1 - min(100, max(0, raw)) / 100)
                            let x = size.width * CGFloat(index) / CGFloat(values.count - 1)
                            if index == 0 { path.move(to: CGPoint(x: x, y: y)) }
                            else { path.addLine(to: CGPoint(x: x, y: y)) }
                        }
                    }.stroke(tint, style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
                }
            }
        }
        .accessibilityLabel("Recent performance samples")
    }
}

struct HardwareAtlas: NSViewRepresentable {
    let snapshot: SystemSnapshot
    let onSelection: (String) -> Void
    func makeCoordinator() -> Coordinator { Coordinator(onSelection: onSelection) }
    func makeNSView(context: Context) -> WKWebView {
        let controller = WKUserContentController()
        controller.add(context.coordinator, name: "mysystem")
        let config = WKWebViewConfiguration()
        config.userContentController = controller
        config.defaultWebpagePreferences.allowsContentJavaScript = true
        let web = WKWebView(frame: .zero, configuration: config)
        web.navigationDelegate = context.coordinator
        web.setValue(false, forKey: "drawsBackground")
        context.coordinator.webView = web
        if let folder = Bundle.main.resourceURL?.appendingPathComponent("Scene", isDirectory: true) {
            let index = folder.appendingPathComponent("index.html")
            if FileManager.default.fileExists(atPath: index.path) {
                web.loadFileURL(index, allowingReadAccessTo: folder)
            } else {
                web.loadHTMLString("<h2>3D scene assets unavailable</h2><p>Native monitoring is still running.</p>", baseURL: nil)
            }
        }
        return web
    }
    func updateNSView(_ view: WKWebView, context: Context) {
        context.coordinator.onSelection = onSelection
        context.coordinator.send(snapshot)
    }
    final class Coordinator: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
        weak var webView: WKWebView?
        var ready = false
        var onSelection: (String) -> Void
        init(onSelection: @escaping (String) -> Void) { self.onSelection = onSelection }
        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            ready = true
        }
        func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction,
                     decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            let allowed = action.request.url?.isFileURL == true
            decisionHandler(allowed ? .allow : .cancel)
        }
        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard let data = message.body as? [String: Any], data["type"] as? String == "select",
                  let name = data["component"] as? String,
                  ["overview", "cpu", "gpu", "memory", "storage", "motherboard", "cooling"].contains(name) else { return }
            DispatchQueue.main.async { [weak self] in self?.onSelection(name) }
        }
        func send(_ snapshot: SystemSnapshot) {
            guard ready, let webView else { return }
            let payload: [String: Any] = [
                "type": "telemetry",
                "metrics": snapshot.readings.map { item in
                    ["id": item.id, "value": item.value.map { $0 as Any } ?? NSNull(),
                     "unit": item.unit, "availability": item.value == nil ? "Unavailable" : "Available"] as [String: Any]
                },
                "thresholds": ["cpu": 85, "gpu": 83]
            ]
            guard let data = try? JSONSerialization.data(withJSONObject: payload),
                  let json = String(data: data, encoding: .utf8) else { return }
            webView.evaluateJavaScript("window.__mySystemNativeReceive?.(\(json));", completionHandler: nil)
        }
    }
}
