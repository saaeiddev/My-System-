import AppKit
import SceneKit
import SwiftUI

/// Fully native hardware atlas. Unlike WKWebView/WebGL it does not depend on
/// JavaScript file-origin restrictions or a WebKit rendering process.
struct NativeHardwareAtlas: NSViewRepresentable {
    let snapshot: SystemSnapshot
    let onSelection: (String) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(onSelection) }

    func makeNSView(context: Context) -> NativeSCNView {
        let view = NativeSCNView(frame: .zero)
        view.backgroundColor = NSColor(calibratedRed: 0.055, green: 0.085, blue: 0.105, alpha: 1)
        view.antialiasingMode = .multisampling4X
        view.allowsCameraControl = true
        view.autoenablesDefaultLighting = false
        view.preferredFramesPerSecond = 30
        view.scene = MacHardwareModel.makeScene()
        view.pointOfView = view.scene?.rootNode.childNode(withName: "camera", recursively: false)
        view.onPick = { [weak coordinator = context.coordinator] name in coordinator?.onSelection(name) }
        view.updateMetrics(snapshot)
        return view
    }

    func updateNSView(_ view: NativeSCNView, context: Context) {
        context.coordinator.onSelection = onSelection
        view.updateMetrics(snapshot)
    }

    final class Coordinator {
        var onSelection: (String) -> Void
        init(_ onSelection: @escaping (String) -> Void) { self.onSelection = onSelection }
    }
}

final class NativeSCNView: SCNView {
    var onPick: ((String) -> Void)?
    private var mouseStart: NSPoint?
    private let selectable: Set<String> = ["cpu", "gpu", "memory", "storage", "motherboard", "cooling"]

    override func mouseDown(with event: NSEvent) {
        mouseStart = convert(event.locationInWindow, from: nil)
        super.mouseDown(with: event)
    }

    override func mouseUp(with event: NSEvent) {
        defer { super.mouseUp(with: event); mouseStart = nil }
        guard let start = mouseStart else { return }
        let end = convert(event.locationInWindow, from: nil)
        guard hypot(end.x - start.x, end.y - start.y) < 5 else { return }
        let result = hitTest(end, options: [.firstFoundOnly: false])
        for hit in result {
            var node: SCNNode? = hit.node
            while let current = node {
                if let name = current.name, selectable.contains(name) {
                    onPick?(name)
                    return
                }
                node = current.parent
            }
        }
    }

    func updateMetrics(_ snapshot: SystemSnapshot) {
        guard let group = scene?.rootNode.childNode(withName: "cpu", recursively: false),
              let indicator = group.childNode(withName: "cpu-activity-light", recursively: true),
              let material = indicator.geometry?.firstMaterial else { return }
        let cpu = snapshot.cpu ?? 0
        material.emission.contents = NSColor(calibratedRed: 0.27 + min(cpu / 120, 0.6),
                                             green: 0.70, blue: 0.62, alpha: 1)
        material.emission.intensity = snapshot.cpu == nil ? 0.05 : CGFloat(0.18 + cpu / 180)
    }
}

enum MacHardwareModel {
    static func material(_ hex: UInt32, metal: CGFloat = 0.5, rough: CGFloat = 0.35,
                         glow: CGFloat = 0) -> SCNMaterial {
        let color = NSColor(calibratedRed: CGFloat((hex >> 16) & 0xff)/255,
                            green: CGFloat((hex >> 8) & 0xff)/255,
                            blue: CGFloat(hex & 0xff)/255, alpha: 1)
        let m = SCNMaterial()
        m.diffuse.contents = color
        m.lightingModel = .physicallyBased
        m.metalness.contents = metal
        m.roughness.contents = rough
        m.isDoubleSided = true
        if glow > 0 {
            m.emission.contents = color
            m.emission.intensity = glow
        }
        return m
    }

    static func box(_ parent: SCNNode, _ name: String, w: CGFloat, h: CGFloat, d: CGFloat,
                    at x: Float, _ y: Float, _ z: Float, color: UInt32, metal: CGFloat = 0.6) -> SCNNode {
        let g = SCNBox(width: w, height: h, length: d, chamferRadius: min(w,h,d) * 0.07)
        g.materials = [material(color, metal: metal)]
        let n = SCNNode(geometry: g)
        n.name = name
        n.position = SCNVector3(x, y, z)
        parent.addChildNode(n)
        return n
    }

    static func cylinder(_ parent: SCNNode, _ name: String, radius: CGFloat, h: CGFloat,
                         at x: Float, _ y: Float, _ z: Float, color: UInt32) -> SCNNode {
        let g = SCNCylinder(radius: radius, height: h)
        g.radialSegmentCount = 48
        g.materials = [material(color)]
        let n = SCNNode(geometry: g)
        n.name = name
        n.position = SCNVector3(x,y,z)
        parent.addChildNode(n)
        return n
    }

    static func torus(_ parent: SCNNode, _ name: String, radius: CGFloat,
                      at x: Float, _ y: Float, _ z: Float, color: UInt32) {
        let g = SCNTorus(ringRadius: radius, pipeRadius: 0.045)
        g.ringSegmentCount = 52
        g.pipeSegmentCount = 12
        g.materials = [material(color, metal: 0.7)]
        let n = SCNNode(geometry: g)
        n.name = name
        n.position = SCNVector3(x,y,z)
        n.eulerAngles.x = .pi / 2
        parent.addChildNode(n)
    }

    static func group(_ scene: SCNScene, _ name: String) -> SCNNode {
        let n = SCNNode()
        n.name = name
        scene.rootNode.addChildNode(n)
        return n
    }

    static func makeScene() -> SCNScene {
        let scene = SCNScene()
        scene.background.contents = NSColor(calibratedRed: 0.055, green: 0.085, blue: 0.105, alpha: 1)
        scene.fogColor = NSColor(calibratedRed: 0.055, green: 0.085, blue: 0.105, alpha: 1)
        scene.fogStartDistance = 21
        scene.fogEndDistance = 44

        let board = group(scene, "motherboard")
        box(board, "graphite-pcb", w: 10.1,h: 0.18,d: 6.7,at: 0,0,0,color:0x132d38)
        box(board, "silver-edge", w: 10.2,h:0.09,d:6.78,at:0,-0.14,0,color:0x657b89)
        for i in 0..<23 {
            let x = Float(i % 12) * 0.70 - 3.95
            let z = Float(i / 12) * 0.75 + 2.40
            box(board, "circuit-trace", w:0.53,h:0.009,d:0.025,at:x,0.105,z,color:0x3b776e,metal:0.85)
        }
        for i in 0..<12 {
            let x = Float(i % 6) * 0.32 + 2.45
            let z = Float(i / 6) * 0.30 - 2.9
            box(board, "chipset", w:0.23,h:0.14,d:0.19,at:x,0.19,z,color:0x202c37)
        }
        for i in 0..<5 {
            let x = -4.8 + Float(i) * 2.35
            box(board, "standoff", w:0.16,h:0.27,d:0.16,at:x,-0.25,3.08,color:0x859da6)
        }

        let cpu = group(scene, "cpu")
        box(cpu, "cpu-socket", w:2.1,h:0.20,d:2.05,at:-1.85,0.26,-0.98,color:0x344454)
        box(cpu, "cpu-retainer", w:1.72,h:0.14,d:1.69,at:-1.85,0.46,-0.98,color:0x9eaeb6)
        box(cpu, "cpu-heatspreader", w:1.35,h:0.19,d:1.31,at:-1.85,0.62,-0.98,color:0xd2d8d2)
        box(cpu, "cpu-activity-light", w:1.48,h:0.023,d:0.07,at:-1.85,0.745,-0.29,color:0x50d9c3)
        for i in 0..<28 {
            let x = -2.48 + Float(i % 7) * 0.21
            let z = -1.57 + Float(i / 7) * 0.37
            box(cpu, "socket-contact", w:0.055,h:0.015,d:0.055,at:x,0.73,z,color:0xf0c98c)
        }

        let cooling = group(scene, "cooling")
        box(cooling, "cooler-base", w:1.40,h:0.13,d:1.38,at:-1.85,0.85,-0.98,color:0x9ba8aa)
        for i in 0..<15 {
            box(cooling, "tower-fin", w:1.48,h:0.045,d:1.25,
                at:-1.85,1.05+Float(i)*0.095,-0.98,color:0x90a6b2)
        }
        for i in 0..<4 {
            cylinder(cooling, "heat-pipe", radius:0.075,h:1.7,
                     at:-2.35 + Float(i)*0.33, 1.20, -0.98,color:0xc98a53)
        }
        // Additional top-facing fan behind the CPU tower.
        box(cooling, "system-fan-frame", w:1.30,h:0.16,d:1.30,at:-3.55,0.30,-2.25,color:0x303c46)
        cylinder(cooling, "fan-hub", radius:0.23,h:0.19,at:-3.55,0.47,-2.25,color:0x778e99)
        torus(cooling, "fan-ring", radius:0.50,at:-3.55,0.46,-2.25,color:0x50d9c3)
        for i in 0..<7 {
            let blade = box(cooling, "fan-blade", w:0.15,h:0.06,d:0.38,
                at:-3.55,0.48,-1.94,color:0x5a6774)
            let angle = Float(i)*Float.pi*2/7
            blade.position = SCNVector3(-3.55 + sin(angle)*0.30, 0.48,
                                        -2.25 + cos(angle)*0.30)
            blade.eulerAngles.y = angle
        }

        let gpu = group(scene, "gpu")
        box(gpu, "graphics-card-body", w:5.85,h:0.33,d:1.78,at:1.12,0.65,1.25,color:0x394550)
        box(gpu, "graphics-card-backplate", w:5.75,h:0.07,d:1.68,at:1.12,0.87,1.25,color:0x83959c)
        box(gpu, "gpu-edge-lit", w:5.7,h:0.04,d:0.055,at:1.12,0.90,0.35,color:0x56cbbd)
        for x in [-0.70, 1.12, 2.94] as [Float] {
            cylinder(gpu, "graphics-fan",radius:0.68,h:0.08,at:x,0.98,1.25,color:0x18242d)
            torus(gpu, "graphics-fan-rim", radius:0.58,at:x,1.04,1.25,color:0x81989d)
            cylinder(gpu,"fan-hub",radius:0.16,h:0.10,at:x,1.07,1.25,color:0x50b8ab)
            for b in 0..<7 {
                let angle = Float(b)*Float.pi*2/7
                let blade=box(gpu,"blade",w:0.12,h:0.04,d:0.34,
                    at:x+sin(angle)*0.31,1.04,1.25+cos(angle)*0.31,color:0x4c5e68)
                blade.eulerAngles.y=angle
            }
        }
        for i in 0..<14 {
            box(gpu, "heatsink-fin", w:0.025,h:0.18,d:1.50,
                at:-1.55+Float(i)*0.40,0.43,1.25,color:0x9aabb2)
        }

        let memory = group(scene, "memory")
        for i in 0..<4 {
            let x = -4.3+Float(i)*0.34
            box(memory, "dimm-socket", w:0.27,h:0.13,d:4.0,at:x,0.27,0.35,color:0x687a86)
            if i % 2 == 0 {
                box(memory, "memory-module", w:0.12,h:0.80,d:3.70,
                    at:x,0.70,0.35,color:0x214d46)
                box(memory, "module-top", w:0.16,h:0.10,d:3.72,
                    at:x,1.16,0.35,color:0x50d9c3)
                for k in 0..<7 {
                    box(memory, "memory-chip", w:0.17,h:0.32,d:0.28,
                        at:x,0.68,-1.12+Float(k)*0.47,color:0x111d26)
                }
            }
        }

        let storage = group(scene, "storage")
        box(storage, "nvme-socket", w:3.1,h:0.08,d:0.62,at:2.65,0.23,-1.60,color:0x8f9b9d)
        box(storage, "nvme", w:2.75,h:0.11,d:0.43,at:2.55,0.32,-1.60,color:0x2b5d4e)
        for i in 0..<5 {
            box(storage,"nand-chip",w:0.32,h:0.06,d:0.29,at:1.56+Float(i)*0.43,0.42,-1.60,color:0x17232c)
        }
        box(storage, "solid-state-drive", w:1.62,h:0.35,d:1.33,at:3.70,0.35,-2.55,color:0x697985)
        box(storage, "drive-label", w:1.15,h:0.02,d:0.72,at:3.70,0.56,-2.55,color:0x263e46)

        // Corner column LEDs and supporting bottom surface.
        for x in [-4.55,4.55] as [Float] {
            for z in [-3.05,3.05] as [Float] {
                cylinder(board,"mount-bolt",radius:0.09,h:0.05,at:x,0.14,z,color:0xd6ab71)
            }
        }
        let floor = SCNFloor()
        floor.reflectivity = 0.12
        floor.firstMaterial = material(0x121b22, metal:0.25, rough:0.55)
        let floorNode = SCNNode(geometry: floor)
        floorNode.position.y = -0.38
        scene.rootNode.addChildNode(floorNode)
        for i in -10...10 {
            let coordinate = Float(i) * 0.70
            box(scene.rootNode,"stage-grid",w:0.008,h:0.01,d:15.5,
                at:coordinate,-0.369,0,color:0x223d46,metal:0)
            box(scene.rootNode,"stage-grid",w:15.5,h:0.01,d:0.008,
                at:0,-0.367,coordinate,color:0x223d46,metal:0)
        }

        let ambient = SCNLight()
        ambient.type = .ambient
        ambient.color = NSColor(calibratedWhite: 0.52, alpha: 1)
        ambient.intensity = 460
        let ambientNode = SCNNode()
        ambientNode.light = ambient
        scene.rootNode.addChildNode(ambientNode)
        let key = SCNLight()
        key.type = .omni
        key.color = NSColor(calibratedRed: 0.96, green: 0.98, blue: 1, alpha: 1)
        key.intensity = 1900
        key.castsShadow = true
        key.shadowRadius = 8
        key.shadowSampleCount = 8
        let keyNode = SCNNode(light:key)
        keyNode.position = SCNVector3(-4,11,11)
        scene.rootNode.addChildNode(keyNode)
        let fill = SCNLight()
        fill.type = .omni
        fill.color = NSColor(calibratedRed: 0.31, green: 0.89, blue: 0.79, alpha: 1)
        fill.intensity = 750
        let fillNode = SCNNode(light:fill)
        fillNode.position = SCNVector3(7,6,-6)
        scene.rootNode.addChildNode(fillNode)

        let target = SCNNode()
        target.name = "camera-target"
        target.position = SCNVector3(0, 0.2, 0)
        scene.rootNode.addChildNode(target)
        let camera = SCNCamera()
        camera.zNear = 0.1
        camera.zFar = 80
        camera.fieldOfView = 48
        camera.wantsHDR = true
        camera.exposureOffset = -0.2
        let cameraNode = SCNNode()
        cameraNode.name = "camera"
        cameraNode.camera = camera
        cameraNode.position = SCNVector3(10,10,15)
        let aim = SCNLookAtConstraint(target: target)
        aim.isGimbalLockEnabled = true
        cameraNode.constraints = [aim]
        scene.rootNode.addChildNode(cameraNode)
        return scene
    }
}
