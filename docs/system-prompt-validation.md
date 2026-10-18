# System prompt validation

Run the bridge integration tests with Node.js 18 or newer:

```powershell
node --test .\tests\system-prompt.test.cjs
```

The tests use a mock upstream on temporary loopback ports and remove their temporary files. They check UTF-8 multiline prompts, explicit client overrides, hot reload and clear, OpenAI image/tool payload preservation, Ollama chat/generate conversion, streaming, split UTF-8 request chunks, model discovery, and visible configuration/upstream errors. They do not load a model.

`tests/PromptFormCheck.cs` is a .NET Framework Windows Forms harness. Compile it as an executable into an isolated directory alongside the built `KVMem.exe`, referencing `System.Windows.Forms.dll` and `System.Drawing.dll`. It invokes the prompt form's save method, reopens the form, verifies multiline and clear persistence, and checks the backup. Use an isolated directory because this harness writes `system-prompt.json` there. An existing settings/profile pair may be copied into the isolated directory to verify compatibility; the prompt save must leave their hashes unchanged.

The launcher's existing `--preview` mode renders every tab, including the system prompt tab. The checked-in screenshot was inspected for layout. Browser chat was checked with a mock streaming response and confirmed the default system message arrived upstream. Real-model generation was not repeated for this change.

`package.ps1` builds v1.2.0 with blank example settings. The archive includes `chat.html` and `system-prompt.cjs` and excludes local prompt/settings/profile data. Verify the ZIP against its adjacent SHA-256 file before distribution.
