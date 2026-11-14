import SwiftUI

struct ApprovalView: View {
    @EnvironmentObject private var model: LinkModel
    let session: String
    let approval: [String: Any]
    @State private var expanded = false
    @State private var responseText = "{}"
    @State private var answers = [String: String]()
    @State private var submitting = false
    private var method: String { approval["method"] as? String ?? "" }
    private var parameters: [String: Any] { approval["parameters"] as? [String: Any] ?? [:] }
    private var questions: [[String: Any]] { parameters["questions"] as? [[String: Any]] ?? [] }
    private var resolving: Bool { approval["resolving"] as? Bool ?? false }
    var body: some View {
        VStack(alignment:.leading, spacing:10) {
            Label(approval["summary"] as? String ?? "Approval requested", systemImage:"hand.raised.fill").font(.headline).textSelection(.enabled)
            if method.contains("requestUserInput") {
                ForEach(questions.indices, id:\.self) { i in
                    let question = questions[i]; let id = question["id"] as? String ?? String(i)
                    Text(question["question"] as? String ?? id).font(.callout)
                    if let options = question["options"] as? [[String: Any]], !options.isEmpty {
                        Picker("Answer", selection:Binding(get:{ answers[id] ?? "" }, set:{ answers[id] = $0 })) {
                            Text("Choose…").tag("")
                            ForEach(options.indices, id:\.self) { n in Text(options[n]["label"] as? String ?? "").tag(options[n]["label"] as? String ?? "") }
                        }
                    }
                    if question["isSecret"] as? Bool == true {
                        SecureField("Answer", text:Binding(get:{ answers[id] ?? "" }, set:{ answers[id] = $0 }))
                    } else { TextField("Answer", text:Binding(get:{ answers[id] ?? "" }, set:{ answers[id] = $0 })).textFieldStyle(.roundedBorder) }
                }
            }
            if method.contains("elicitation") {
                Text(parameters["message"] as? String ?? "Provide a JSON response matching the requested schema.").font(.callout)
                TextEditor(text:$responseText).font(.system(.caption, design:.monospaced)).frame(minHeight:70)
            }
            DisclosureGroup("Request details", isExpanded:$expanded) {
                ScrollView { Text(pretty(parameters)).font(.system(.caption, design:.monospaced)).textSelection(.enabled).frame(maxWidth:.infinity, alignment:.leading) }.frame(maxHeight:160)
            }
            HStack {
                Button("Decline", role:.destructive) { respond("decline") }
                Spacer()
                Button(submitting || resolving ? "Resolving…" : "Approve / Submit") { respond("accept") }.buttonStyle(.borderedProminent)
            }.disabled(!model.canInput || submitting || resolving)
        }.padding(12).background(Color.orange.opacity(0.10)).clipShape(RoundedRectangle(cornerRadius:8))
    }
    private func pretty(_ value: Any) -> String { (try? JSONSerialization.data(withJSONObject:value, options:[.prettyPrinted, .sortedKeys])).flatMap { String(data:$0, encoding:.utf8) } ?? "{}" }
    private func respond(_ decision: String) {
        var value: [String: Any] = ["sessionId":session, "requestId":approval["id"] as? String ?? "", "decision":decision]
        if method.contains("requestUserInput") {
            if decision == "accept" && questions.contains(where:{ (answers[$0["id"] as? String ?? ""] ?? "").isEmpty }) { model.error = "Answer each question before submitting."; return }
            var values = [String: Any]()
            for question in questions { if let id = question["id"] as? String { values[id] = ["answers":decision == "accept" ? [answers[id] ?? ""] : []] } }
            value["response"] = ["answers":values]
        } else if method.contains("elicitation") {
            do {
                let content = try JSONSerialization.jsonObject(with:Data(responseText.utf8))
                value["response"] = decision == "accept" ? ["action":"accept", "content":content] : ["action":"decline"]
            } catch { model.error = "Enter valid JSON for this tool response."; return }
        }
        submitting = true
        model.action("approve", value) { _ in submitting = false }
    }
}
