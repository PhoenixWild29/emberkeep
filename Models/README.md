# Models

The `.gguf` model weights are **not committed** to this repo (gitignored + LFS-gated). Each developer downloads them locally.

## Required model (MVP)

**Llama-3.2-3B-Instruct, Q4_K_M quantization** (~2.0 GB on disk).

Source: [bartowski/Llama-3.2-3B-Instruct-GGUF](https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF) on Hugging Face.

Download the file `Llama-3.2-3B-Instruct-Q4_K_M.gguf` and place it in this directory:

```
Models/Llama-3.2-3B-Instruct-Q4_K_M.gguf
```

The native plugin's `ek_init` is called with the absolute path to this file at scene-load.

## License

The Llama-3.2 weights are subject to [Meta's Llama 3.2 Community License](https://www.llama.com/llama3_2/license/). Review the terms before redistribution.

## Stretch model (speculative decoding)

If implementing the speculative decoding stretch goal (spec §7.1), also download:

**Llama-3.2-1B-Instruct, Q4_K_M** as the draft model.
Source: [bartowski/Llama-3.2-1B-Instruct-GGUF](https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF).
