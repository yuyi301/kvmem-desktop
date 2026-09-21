# Vision and 256K validation

Tested with `RVN-IQ3_XXS-mtp.gguf`, `mmproj-Qwen3.8-27B-Q8_0.gguf`, a 16 GB NVIDIA GPU, 64 GB system RAM, CPU vision encoding, and a 262,144-token KVMem context.

| Metric | Result |
|---|---:|
| Prompt tokens | 261,382 |
| Image tokens | 300 |
| Completion tokens | 47 |
| Total context tokens | 261,429 |
| End-to-end time | 354.95 s |
| Fresh prefill | 353.37 s |
| CPU vision encoding | 5.52 s |
| Decode speed | 57.54 tokens/s |
| VRAM after request | 15,217 / 16,376 MiB |

The image contained `KVMEM 7392`, a red square, and a blue circle. The model returned all four facts correctly after the long prompt.

The text was deliberately repetitive and the request disabled thinking. This validates that the vision projector, KVMem paging, and near-capacity context can operate together; it is not a benchmark of document retrieval quality.

