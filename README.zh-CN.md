# KVMem Desktop

这是一个用于 [`kvmem-llama.cpp`](https://github.com/kvmem/kvmem-llama.cpp) 的 Windows 桌面启动器和局域网控制面板。

它可以启动/停止 KVMem 模型服务、配置 256K 上下文、打开网页聊天、显示运行日志，并为局域网内的 Mac 提供 OpenAI 兼容地址和文本版 Ollama 兼容地址。

## 使用

1. 下载 Release 压缩包并完整解压。
2. 准备好 Windows 版 KVMem 运行包和兼容的 GGUF 模型。
3. 打开 `KVMem.exe`，在“启动设置”中选择 `llama-kvmem-server.exe` 和模型文件。
4. 保存，然后点击“启动服务”。
5. 本机可打开 `http://127.0.0.1:18200/`；Mac 使用应用显示的局域网地址。

模型权重和 KVMem 运行库不包含在本项目中。Ollama 兼容桥只是把文本请求转发给同一份 KVMem 模型，不会重复占用模型显存，也不是 Ollama 原生 KVMem 后端。

DSH Desktop 应使用 `openai-completions` 协议、应用显示的 `http://...:18200/v1` 地址和 GGUF 文件名作为模型 ID。推荐兼容设置：

```yaml
compat:
  supportsDeveloperRole: false
  maxTokensField: max_tokens
```

局域网访问需要点击应用里的“允许局域网访问”并确认 Windows 管理员提示。服务没有身份认证，请仅在可信私有局域网中使用，不要转发到公网。

详细依赖、API、性能数据和构建方法请阅读 [README.md](README.md)。

