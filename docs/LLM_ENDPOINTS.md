# LLM endpoint 설정

기본값은 `Disabled`입니다. 관리형 환경 모드에서는 사용자가 명시적으로 켜기 전까지 LLM을 호출하지 않습니다.

## Provider

| Provider | 용도 | Endpoint 예시 |
| --- | --- | --- |
| `Disabled` | rule-only 안전 모드 | 없음 |
| `Ollama` | Ollama `/api/chat` | `http://localhost:11434` |
| `OpenAiCompatible` | vLLM/OpenAI-compatible `/v1/chat/completions` | `http://localhost:8000` |

## Ollama 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "Ollama",
  "LlmEndpoint": "http://localhost:11434",
  "LlmModel": "qwen3.6"
}
```

## vLLM/OpenAI-compatible 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OpenAiCompatible",
  "LlmEndpoint": "http://localhost:8000",
  "LlmModel": "qwen3.6",
  "LlmApiKey": null,
  "LlmApiKeyEnvironmentVariable": "OPENAI_API_KEY"
}
```

`LlmApiKeyEnvironmentVariable`를 쓰면 설정 파일에 토큰을 직접 쓰지 않고 Windows 환경 변수에서 읽습니다.

## 보안 원칙

- prompt와 raw mail body는 저장하지 않습니다.
- SQLite에는 source hash, 짧은 제목/사유/근거 snippet만 저장합니다.
- 외부 네트워크 LLM은 승인된 보안 정책이 허용할 때만 켭니다.
- LLM JSON 파싱이 실패하면 rule-based analyzer로 fallback합니다.
