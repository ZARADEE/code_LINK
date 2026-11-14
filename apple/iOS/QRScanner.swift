import SwiftUI
import AVFoundation

struct QRScanner: View {
    @Environment(\.dismiss) private var dismiss
    let onCode: (String) -> Void
    @State private var error = ""
    var body: some View {
        NavigationStack {
            ZStack(alignment:.bottom) {
                CameraView(onCode:onCode, onError:{ error = $0 }).ignoresSafeArea(edges:.bottom)
                Text(error.isEmpty ? "Point the camera at the Code Link pairing QR code." : error)
                    .foregroundStyle(.white).padding().frame(maxWidth:.infinity).background(.black.opacity(0.7))
            }.navigationTitle("Scan pairing code").toolbar { Button("Cancel") { dismiss() } }
        }
    }
}
private struct CameraView: UIViewControllerRepresentable {
    let onCode: (String) -> Void
    let onError: (String) -> Void
    func makeUIViewController(context: Context) -> CameraController { let c = CameraController(); c.onCode = onCode; c.onError = onError; return c }
    func updateUIViewController(_ uiViewController: CameraController, context: Context) {}
    static func dismantleUIViewController(_ controller: CameraController, coordinator: ()) { controller.stop() }
}
private final class CameraController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    var onCode: ((String) -> Void)?
    var onError: ((String) -> Void)?
    private let session = AVCaptureSession()
    private let queue = DispatchQueue(label:"com.codelink.camera")
    private var preview: AVCaptureVideoPreviewLayer?
    private var found = false
    private var stopped = false
    override func viewDidLoad() {
        super.viewDidLoad(); view.backgroundColor = .black
        AVCaptureDevice.requestAccess(for:.video) { granted in
            guard granted else { DispatchQueue.main.async { self.onError?("Camera access is disabled. Enable it in Settings, or paste the invitation.") }; return }
            self.queue.async { self.configure() }
        }
    }
    private func configure() {
        guard !stopped else { return }
        do {
            guard let camera = AVCaptureDevice.default(for:.video) else { throw LinkError.message("No camera is available. Paste the invitation instead.") }
            let input = try AVCaptureDeviceInput(device:camera)
            let output = AVCaptureMetadataOutput()
            guard session.canAddInput(input), session.canAddOutput(output) else { throw LinkError.message("Unable to start the camera.") }
            session.addInput(input); session.addOutput(output); output.setMetadataObjectsDelegate(self, queue:.main); output.metadataObjectTypes = [.qr]
            DispatchQueue.main.async {
                let layer = AVCaptureVideoPreviewLayer(session:self.session); layer.videoGravity = .resizeAspectFill; layer.frame = self.view.bounds
                self.view.layer.addSublayer(layer); self.preview = layer
            }
            session.startRunning()
        } catch { DispatchQueue.main.async { self.onError?(error.localizedDescription) } }
    }
    override func viewDidLayoutSubviews() { super.viewDidLayoutSubviews(); preview?.frame = view.bounds }
    func stop() { queue.async { self.stopped = true; self.session.stopRunning() } }
    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        guard !found else { return }
        for item in metadataObjects {
            if let value = (item as? AVMetadataMachineReadableCodeObject)?.stringValue, (try? PairingInvite(text:value)) != nil {
                found = true; stop(); onCode?(value); return
            }
        }
    }
}
