# LLM endpoint 설정

기본값은 LLM OFF이며 endpoint/model 입력칸도 비워둡니다. 사용자가 명시적으로 켜기 전까지 LLM을 호출하지 않습니다. LLM을 켠 경우에는 규칙 기반 분석보다 LLM을 먼저 시도합니다. 앱 UI에서는 토글로 ON/OFF를 정하고, provider 드롭다운에는 실제 endpoint 방식만 표시합니다.

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
  "LlmFallbackPolicy": "LlmOnly",
  "LlmInitialConcurrency": 2,
  "LlmMaxConcurrency": 4
}
```

Ollama native 호출은 업무 triage에 맞춰 다음을 기본 적용합니다.

- `think=false`: Qwen 계열처럼 thinking-capable 모델이 긴 내부 reasoning을 하느라 느려지는 것을 줄입니다.
- `format=json`: JSON object 출력을 요구합니다.
- `num_ctx=32768`: 서버 전역 context가 더 크더라도 MailWhere 업무 triage 요청은 32K context로 제한해 parallel slot별 KV/cache 예약량을 줄입니다.
- `num_predict=clamp(256 + batchCount * 160, 512, 4096)`: batch 크기에 맞춰 JSON 출력 예산을 늘리되 과도한 생성을 막습니다.
- `temperature=0.1`, `top_p=0.9`: strict JSON 분류 안정성을 유지하면서 너무 경직된 샘플링은 피합니다.
- `keep_alive=30m`: 대량 스캔 중 모델이 자주 unload되는 것을 줄입니다.

초기/대량 스캔에서는 기본 최대 12건 batch 단위로 여러 메일을 한 번에 분석하고, 메일 본문 길이에 따라 8/4/2/1건으로 자동 축소합니다. scan loop는 준비/중복 확인과 저장을 직렬로 유지하되, 준비된 LLM batch 분석 요청만 기본 2개 동시로 보냅니다. 고급 설정 파일에서 `LlmInitialConcurrency`와 `LlmMaxConcurrency`를 조정할 수 있으며, v0.5.0에서는 둘 다 1~4로 clamp되고 effective concurrency는 `min(initial,max)`입니다. 각 메일 결과는 독립 JSON item으로 매핑하며, 마지막 batch가 작거나 모델이 일부 id를 빠뜨려도 전체 스캔을 실패시키지 않고 누락 item만 다시 시도 가능한 AI 분석 실패 항목으로 남깁니다.

프롬프트는 cache locality를 고려해 system prompt에 고정 정책/스키마를 두고, user payload는 짧은 metadata 뒤 긴 본문을 마지막 블록에 둡니다. 단일 분석은 final `content`, batch 분석은 final `contents[]`를 사용하며, batch의 `items[]` metadata와 `contents[]` body는 같은 `id`로 연결합니다. 매 호출마다 크게 바뀌는 `now` timestamp 대신 `analysisDate`, `timezone`, `utcOffset`만 전달합니다.

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

인증이 필요한 endpoint는 설정 창의 **AI 분석 > 인증**에서 고릅니다.

- `인증 없음`: Ollama/local endpoint처럼 token이 필요 없는 경우.
- `API 키 입력`: 개인 PC에서 바로 테스트할 때 사용합니다. 화면에는 키를 다시 노출하지 않고, probe/진단 로그에도 쓰지 않습니다.
- `환경 변수에서 읽기`: `OPENAI_API_KEY`처럼 Windows 환경 변수에 저장된 값을 참조합니다.

`LlmApiKeyEnvironmentVariable`는 브라우저 로그인이나 Enterprise 계정 재사용 기능이 아닙니다. 로컬/내부 서버가 인증 키를 요구할 때 키 값 대신 Windows 환경 변수 이름으로 참조하기 위한 옵션입니다. `LlmApiKey`가 함께 있으면 직접 입력한 키가 우선합니다.

## Fallback 정책

| 값 | 의미 | 추천 상황 |
| --- | --- | --- |
| `LlmOnly` | LLM이 실패하면 자동 등록하지 않고 확인 필요에 “LLM 분석 실패” 항목으로 남김 | 기본값. rule 오탐 없이 endpoint 품질을 먼저 확인하려는 경우 |
| `LlmThenRules` | LLM을 먼저 호출하고 실패/invalid JSON/timeout이면 규칙 기반 analyzer로 fallback | 사용자가 명시적으로 fallback을 허용한 경우 |

스캔 후 앱 상태에는 `LLM 요청/항목/성공/fallback/실패/요청 평균/항목 환산`과 Ollama 응답 메타(총 시간, load, prompt/eval token·duration, thinking 길이)가 compact하게 표시됩니다. 이 통계에는 메일 제목/본문/prompt가 들어가지 않습니다.

LLM 연결 테스트나 스캔 중 LLM 실패가 발생하고 현재 정책이 `LlmOnly`이면, 앱이 “다음 스캔부터 규칙 기반 fallback을 사용할지”를 한 번 물어봅니다. 동의하지 않으면 계속 AI 분석 실패 항목을 확인 필요에 남깁니다. 이 항목은 같은 source에 중복 생성되지 않으며, LLM 연결이 복구되면 확인 필요 창의 **AI 분석 다시 시도**로 원본 메일을 다시 읽어 처리할 수 있습니다. 재분석이 성공하면 기존 실패 항목은 자동으로 정리됩니다.

## 모델 목록 불러오기

앱의 **모델 불러오기** 버튼은 provider에 따라 다음 endpoint를 호출합니다.

- `OllamaNative`: `GET {endpoint}/api/tags` → `models[].name`
- `OpenAiChatCompletions`, `OpenAiResponses`: `GET {endpoint}/v1/models` → `data[].id`

endpoint가 이미 `/v1`로 끝나면 중복으로 `/v1/v1/models`가 되지 않도록 `/models`만 붙입니다. 목록이 비어 있거나 서버가 모델 목록을 제공하지 않으면 모델명을 직접 입력할 수 있습니다.

## 연결 테스트

앱 설정의 **연결 테스트**는 메일 내용이 아닌 작은 JSON probe만 보냅니다.

- 성공: endpoint/model/provider 조합이 JSON object 응답을 반환함
- `not-configured`: provider/model/endpoint가 비어 있거나 LLM이 꺼져 있음
- `invalid-json`: 응답이 JSON object가 아님
- `timeout`: 설정된 timeout 안에 응답하지 않음
- `http-error`: endpoint 연결/HTTP 오류

로컬 30B 이상 모델은 첫 호출이나 긴 메일에서 30초를 넘길 수 있으므로 기본 timeout은 90초입니다. 필요하면 설정에서 30초/1분/1분 30초/3분 중 선택합니다. 사용자가 [스캔 중지]를 누른 cancellation은 timeout과 구분되어 즉시 스캔을 멈춥니다.

## 보안 원칙

- prompt와 raw mail body는 저장하지 않습니다.
- SQLite에는 source hash, 짧은 제목/사유/근거 snippet을 저장합니다. Outlook 원본 메일 열기와 업무보드 한 줄 표기를 위해 새 항목에는 로컬 source id, 보낸 사람 표시명, 수신 시각, 수신/참조 역할도 저장할 수 있으며, source-derived data 삭제/Not-a-task 처리/AI 분석 실패 항목 정리 시 함께 제거하거나 비식별화합니다.
- 외부 네트워크 LLM은 기본 사용 시나리오가 아닙니다. 승인된 보안 정책이 허용할 때만 켭니다.
- LLM JSON 파싱이 실패하면 선택한 `LlmFallbackPolicy`에 따라 확인 필요에 남기거나 rule-based analyzer로 fallback합니다.
