# LLM endpoint 설정

기본값은 `Disabled`입니다. 관리형 환경 모드에서는 사용자가 명시적으로 켜기 전까지 LLM을 호출하지 않습니다. LLM을 켠 경우에는 rule-based 분석보다 LLM을 먼저 시도합니다.

## Provider

| Provider | 용도 | Endpoint 예시 |
| --- | --- | --- |
| `Disabled` | rule-only 안전 모드 | 없음 |
| `OllamaNative` | Ollama native `/api/chat` | `http://localhost:11434` |
| `OpenAiChatCompletions` | OpenAI-compatible `/v1/chat/completions` | `http://localhost:8000` |
| `OpenAiResponses` | OpenAI-compatible `/v1/responses` | `http://localhost:8000` |

기존 설정 파일의 `Ollama`, `OpenAiCompatible` 문자열은 각각 `OllamaNative`, `OpenAiChatCompletions`로 계속 호환됩니다.

## Ollama 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OllamaNative",
  "LlmEndpoint": "http://localhost:11434",
  "LlmModel": "qwen3.6",
  "LlmFallbackPolicy": "LlmThenRules"
}
```

## OpenAI-compatible Chat Completions 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OpenAiChatCompletions",
  "LlmEndpoint": "http://localhost:8000",
  "LlmModel": "qwen3.6",
  "LlmApiKey": null,
  "LlmApiKeyEnvironmentVariable": null,
  "LlmFallbackPolicy": "LlmOnly"
}
```

## OpenAI-compatible Responses 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OpenAiResponses",
  "LlmEndpoint": "http://localhost:8000",
  "LlmModel": "qwen3.6",
  "LlmApiKey": null,
  "LlmApiKeyEnvironmentVariable": null,
  "LlmFallbackPolicy": "LlmOnly"
}
```

`LlmApiKeyEnvironmentVariable`는 브라우저 로그인이나 Enterprise 계정 재사용 기능이 아닙니다. 로컬/내부 서버가 Bearer token을 요구할 때만 설정 파일에 토큰 값을 직접 쓰지 않고 Windows 환경 변수 이름으로 참조하기 위한 고급 옵션입니다.

## Fallback 정책

| 값 | 의미 | 추천 상황 |
| --- | --- | --- |
| `LlmOnly` | LLM이 실패하면 자동 등록하지 않고 검토함에 “LLM 분석 실패” 후보로 남김 | rule 오탐이 싫고 LLM endpoint 품질을 직접 확인하려는 경우 |
| `LlmThenRules` | LLM을 먼저 호출하고 실패/invalid JSON/timeout이면 rule-based analyzer로 fallback | endpoint가 가끔 불안정해도 후속조치 탐지를 계속하고 싶은 경우 |

스캔 후 앱 상태에는 `LLM 시도/성공/fallback/실패/평균 응답시간`이 표시됩니다. 이 통계에는 메일 제목/본문/prompt가 들어가지 않습니다.

## 연결 테스트

앱 설정의 **LLM 연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다.

- 성공: endpoint/model/provider 조합이 JSON object 응답을 반환함
- `not-configured`: provider/model/endpoint가 비어 있거나 LLM이 꺼져 있음
- `invalid-json`: 응답이 JSON object가 아님
- `timeout`: 설정된 timeout 안에 응답하지 않음
- `http-error`: endpoint 연결/HTTP 오류

## 보안 원칙

- prompt와 raw mail body는 저장하지 않습니다.
- SQLite에는 source hash, 짧은 제목/사유/근거 snippet만 저장합니다.
- 외부 네트워크 LLM은 기본 사용 시나리오가 아닙니다. 승인된 보안 정책이 허용할 때만 켭니다.
- LLM JSON 파싱이 실패하면 선택한 `LlmFallbackPolicy`에 따라 검토함에 남기거나 rule-based analyzer로 fallback합니다.
