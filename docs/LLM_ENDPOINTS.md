# LLM endpoint 설정

기본값은 LLM OFF입니다. 사용자가 명시적으로 켜기 전까지 LLM을 호출하지 않습니다. LLM을 켠 경우에는 규칙 기반 분석보다 LLM을 먼저 시도합니다. 앱 UI에서는 토글로 ON/OFF를 정하고, provider 드롭다운에는 실제 endpoint 방식만 표시합니다.

## Provider

| Provider | 용도 | Endpoint 예시 |
| --- | --- | --- |
| `OllamaNative` | Ollama native `/api/chat` | `http://localhost:11434` |
| `OpenAiChatCompletions` | OpenAI-compatible `/v1/chat/completions` | `http://localhost:8000` |
| `OpenAiResponses` | OpenAI-compatible `/v1/responses` | `http://localhost:8000` |

설정 파일 내부의 `Disabled`는 LLM OFF 상태를 뜻합니다. 기존 설정 파일의 `Ollama`, `OpenAiCompatible` 문자열은 각각 `OllamaNative`, `OpenAiChatCompletions`로 계속 호환됩니다.

## Ollama 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OllamaNative",
  "LlmEndpoint": "http://localhost:11434",
  "LlmModel": "",
  "LlmTimeoutSeconds": 90,
  "LlmFallbackPolicy": "LlmOnly"
}
```

Ollama native 호출은 업무 triage에 맞춰 다음을 기본 적용합니다.

- `think=false`: Qwen 계열처럼 thinking-capable 모델이 긴 내부 reasoning을 하느라 느려지는 것을 줄입니다.
- `format=json`: JSON object 출력을 요구합니다.
- `num_predict=768`: 필요한 구조화 결과 이상으로 길게 생성하지 않게 제한합니다.
- `temperature=0`, `top_p=0.9`: 업무 triage 결과가 매번 흔들리지 않도록 보수적으로 샘플링합니다.
- `keep_alive=30m`: 대량 스캔 중 모델이 자주 unload되는 것을 줄입니다.

초기/대량 스캔에서는 기본 8건 batch 단위로 여러 메일을 한 번에 분석합니다. 각 메일 결과는 독립 JSON item으로 매핑하며, 마지막 batch가 8건보다 작거나 모델이 일부 id를 빠뜨려도 전체 스캔을 실패시키지 않고 누락 item만 retry 가능한 LLM 실패 후보로 남깁니다.

## OpenAI-compatible Chat Completions 예시

```json
{
  "ExternalLlmEnabled": true,
  "LlmProvider": "OpenAiChatCompletions",
  "LlmEndpoint": "http://localhost:8000",
  "LlmModel": "",
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
  "LlmModel": "",
  "LlmApiKey": null,
  "LlmApiKeyEnvironmentVariable": null,
  "LlmFallbackPolicy": "LlmOnly"
}
```

`LlmApiKeyEnvironmentVariable`는 브라우저 로그인이나 Enterprise 계정 재사용 기능이 아닙니다. 로컬/내부 서버가 Bearer token을 요구할 때만 설정 파일에 토큰 값을 직접 쓰지 않고 Windows 환경 변수 이름으로 참조하기 위한 고급 옵션입니다.

## Fallback 정책

| 값 | 의미 | 추천 상황 |
| --- | --- | --- |
| `LlmOnly` | LLM이 실패하면 자동 등록하지 않고 검토 후보에 “LLM 분석 실패” 후보로 남김 | 기본값. rule 오탐 없이 endpoint 품질을 먼저 확인하려는 경우 |
| `LlmThenRules` | LLM을 먼저 호출하고 실패/invalid JSON/timeout이면 규칙 기반 analyzer로 fallback | 사용자가 명시적으로 fallback을 허용한 경우 |

스캔 후 앱 상태에는 `LLM 시도/성공/fallback/실패/평균 응답시간`이 표시됩니다. 이 통계에는 메일 제목/본문/prompt가 들어가지 않습니다.

LLM 연결 테스트나 스캔 중 LLM 실패가 발생하고 현재 정책이 `LlmOnly`이면, 앱이 “다음 스캔부터 규칙 기반 fallback을 사용할지”를 한 번 물어봅니다. 동의하지 않으면 계속 LLM 실패 후보를 검토 후보에 남깁니다. 이 후보는 같은 source에 중복 생성되지 않으며, LLM 연결이 복구되어 재분석이 성공하면 자동으로 정리됩니다.

## 모델 목록 불러오기

앱의 **모델 불러오기** 버튼은 provider에 따라 다음 endpoint를 호출합니다.

- `OllamaNative`: `GET {endpoint}/api/tags` → `models[].name`
- `OpenAiChatCompletions`, `OpenAiResponses`: `GET {endpoint}/v1/models` → `data[].id`

endpoint가 이미 `/v1`로 끝나면 중복으로 `/v1/v1/models`가 되지 않도록 `/models`만 붙입니다. 목록이 비어 있거나 서버가 모델 목록을 제공하지 않으면 모델명을 직접 입력할 수 있습니다.

## 연결 테스트

앱 설정의 **LLM 연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다.

- 성공: endpoint/model/provider 조합이 JSON object 응답을 반환함
- `not-configured`: provider/model/endpoint가 비어 있거나 LLM이 꺼져 있음
- `invalid-json`: 응답이 JSON object가 아님
- `timeout`: 설정된 timeout 안에 응답하지 않음
- `http-error`: endpoint 연결/HTTP 오류

로컬 30B 이상 모델은 첫 호출이나 긴 메일에서 30초를 넘길 수 있으므로 기본 timeout은 90초입니다. 필요하면 고급 설정에서 5~180초 범위로 조정합니다. 사용자가 [스캔 중지]를 누른 cancellation은 timeout과 구분되어 즉시 스캔을 멈춥니다.

## 보안 원칙

- prompt와 raw mail body는 저장하지 않습니다.
- SQLite에는 source hash, 짧은 제목/사유/근거 snippet을 저장합니다. Outlook 원본 메일 열기와 업무보드 한 줄 표기를 위해 새 항목에는 로컬 source id, 보낸 사람 표시명, 수신 시각, 수신/참조 역할도 저장할 수 있으며, source-derived data 삭제/Not-a-task 처리/LLM 실패 후보 정리 시 함께 제거하거나 비식별화합니다.
- 외부 네트워크 LLM은 기본 사용 시나리오가 아닙니다. 승인된 보안 정책이 허용할 때만 켭니다.
- LLM JSON 파싱이 실패하면 선택한 `LlmFallbackPolicy`에 따라 검토 후보에 남기거나 rule-based analyzer로 fallback합니다.
