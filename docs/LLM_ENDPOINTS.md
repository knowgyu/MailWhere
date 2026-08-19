# LLM endpoint 설정

기본값은 LLM OFF이며 endpoint/model 입력칸도 비워둡니다. 사용자가 명시적으로 켜기 전까지 LLM을 호출하지 않습니다. LLM을 켠 경우에는 규칙 기반 분석보다 LLM을 먼저 시도합니다. 앱 UI에서는 토글로 ON/OFF를 정하고, provider 드롭다운에는 실제 endpoint 방식만 표시합니다.

## Provider

| Provider | 용도 | Endpoint 예시 |
| --- | --- | --- |
| `OllamaNative` | Ollama native `/api/chat` | `http://localhost:11434` |
| `OpenAiChatCompletions` | OpenAI-compatible `/v1/chat/completions`; vLLM 권장 | `http://localhost:8000` |
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
  "LlmInitialConcurrency": 1,
  "LlmMaxConcurrency": 1
}
```

Ollama native 호출은 업무 triage에 맞춰 다음을 기본 적용합니다.

- `think=false`: Qwen 계열처럼 thinking-capable 모델이 긴 내부 reasoning을 하느라 느려지는 것을 줄입니다.
- `format=json`: JSON object 출력을 요구합니다.
- 기본적으로 `num_ctx`를 보내지 않습니다. 이미 OpenWebUI/CLI/서버 설정으로 VRAM에 올라간 Ollama runner를 다른 context 크기로 다시 로드하지 않기 위해 서버/현재 runner의 context 정책을 따릅니다.
- `num_predict=clamp(256 + batchCount * 160, 512, 4096)`: batch 크기에 맞춰 JSON 출력 예산을 늘리되 과도한 생성을 막습니다.
- `temperature=0.1`, `top_p=0.9`: strict JSON 분류 안정성을 유지하면서 너무 경직된 샘플링은 피합니다.
- 기본적으로 `keep_alive`를 보내지 않습니다. MailWhere가 모델 lifetime을 덮어쓰지 않고 Ollama 서버/OpenWebUI/CLI에서 정한 유지 정책을 그대로 따릅니다.

초기/대량 스캔은 기본 최대 4건 batch로 분석하고, 메일이 길면 2건 또는 1건으로 자동 축소합니다. 준비/중복 확인과 저장은 직렬로 유지하고, LLM batch 요청도 기본 1개씩 보내 로컬 서버를 흔들지 않도록 합니다. 고급 설정 파일의 `LlmInitialConcurrency`와 `LlmMaxConcurrency`는 1~4로 clamp되지만, 측정 근거가 없으면 `1/1`을 유지합니다. transient HTTP/timeout은 한 번 재시도하고, batch JSON이 깨지거나 일부 id가 빠지면 실패한 항목만 1개씩 다시 분석합니다.

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

vLLM에는 Chat Completions를 권장합니다. MailWhere는 단일 결과와 batch `items[]` 결과 각각에 `response_format: json_schema`를 보내 `kind`, `disposition`, `confidence`, `id` 등 출력 계약을 강제합니다. vLLM이 오래되어 `json_schema`를 지원하지 않으면 capability probe 또는 첫 분석에서 HTTP 오류가 나므로, 해당 서버를 업그레이드하거나 Responses provider를 명시적으로 선택합니다.

LLM에 원문 전체를 보내지 않습니다. 현재 메일 본문은 최대 1,300자, 현재 발신자의 명시적 전달/대응 요청이 있을 때만 forwarded context는 최대 900자, reply quoted history는 최대 240자로 제한합니다. 전달 본문만의 요청은 자동 업무로 만들지 않습니다.

## Qwen/Qwen3.8-27B with vLLM

v0.13.0의 Qwen 대상은 `Qwen/Qwen3.8-27B`입니다. 이 release의 설정/운영 안내는 이 27B target만 기준으로 합니다.

권장 vLLM 시작점:

```bash
vllm serve Qwen/Qwen3.8-27B \
  --max-model-len 262144 \
  --reasoning-parser qwen3
```

이 command는 target GPU/vLLM host에서 검증해야 합니다. Linux repo 검증은 문서와 static checks만 수행하고 live vLLM endpoint를 시작하지 않습니다.

운영 기준:

- vLLM recipe 최소 기준은 `vLLM 0.17.0+`입니다.
- `--reasoning-parser qwen3`는 Qwen3 계열 reasoning block을 content와 분리하기 위한 필수 운영 옵션으로 봅니다.
- thinking off 기본값은 request-level `chat_template_kwargs: { "enable_thinking": false }`입니다.
- vLLM `0.25+`에서는 `reasoning_effort="none"`이 `enable_thinking=false`로 매핑될 수 있습니다. MailWhere는 이 경로를 편의 옵션으로만 취급하고, capability probe가 같은 single/batch/대기 종료 판단 shape에서 통과할 때만 사용합니다.
- 정확한 `Qwen/Qwen3.8-27B` target에서 hard thinking-off가 선택되면 Qwen 공식 비사고 profile인 `temperature=0.7`, `top_p=0.8`, `top_k=20`, `presence_penalty=1.5`, `repetition_penalty=1.0`을 요청 단위로 명시합니다. 서버의 사고 모드 generation defaults는 바꾸지 않습니다.
- `min_p=0.0`과 `repetition_penalty=1.0`은 neutral 값입니다. Responses 호환성을 위해 neutral `min_p`는 보내지 않고, production `seed`, `preserve_thinking`, streaming도 사용하지 않습니다.
- JSON Schema가 기본 structured-output mode입니다. JSON Object는 endpoint compatibility가 필요할 때 쓰는 약한 mode이며, schema 검증 강도를 낮춥니다.
- LLM concurrency는 계속 `1/1`입니다. Batch 분석이 실패하거나 일부 id가 빠지면 누락 항목만 1건씩 순서대로 다시 분석하고, 병렬 요청을 시작하지 않습니다.

Provider request body contract:

- Chat Completions sends `temperature`, `max_tokens`, and `response_format`; the Qwen3.8 non-thinking profile also sends `top_p`, `top_k`, `presence_penalty`, and `repetition_penalty`.
- Responses sends `temperature`, `max_output_tokens`, and `text.format`; the same supported Qwen3.8 sampling fields are applied.
- `reasoning_effort="none"` maps to Chat Completions `reasoning_effort` and Responses `reasoning: { "effort": "none" }`.
- Template-native thinking off maps to `chat_template_kwargs: { "enable_thinking": false }`.
- Ollama-only fields such as `think=false` and `options.num_predict` stay on Ollama requests only.

Settings tooltips should keep the user-facing distinction simple:

| Control | Default | Tooltip point |
| --- | --- | --- |
| Thinking control | `enable_thinking=false` | Qwen3.8이 긴 reasoning에 예산을 쓰지 않도록 hard control을 보냅니다. |
| Structured output | JSON Schema | 가장 엄격한 JSON 계약입니다. JSON Object는 호환성 fallback입니다. |
| Temperature | other models `0.1`; Qwen3.8 non-thinking `0.7` | Qwen3.8 target은 공식 비사고 profile을 사용하며 UI 값은 다른 모델에 적용됩니다. |
| Output tokens | bounded preset | Batch 크기에 맞춰 늘리되 truncation을 probe에서 잡습니다. |
| Batch size | small/sequential | 속도보다 endpoint 안정성을 우선하고 retry도 순차 실행합니다. |

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

기본값에서는 fallback 제안 팝업을 띄우지 않습니다. 필요하면 설정의 **AI 실패 시 규칙 fallback 제안 알림 표시**를 켜고, 실제 처리 정책은 `AI 실패 시` 선택값으로 정합니다. `LlmOnly`는 실패를 확인 필요에 보관하며, 이 항목은 같은 source에 중복 생성되지 않습니다. LLM 연결이 복구되면 확인 필요 창의 **실패한 AI 분석 다시 시도**로 원본 메일을 다시 읽어 처리할 수 있고, 성공하면 기존 실패 항목은 정리됩니다.

## 모델 목록 불러오기

앱의 **모델 불러오기** 버튼은 provider에 따라 다음 endpoint를 호출합니다.

- `OllamaNative`: `GET {endpoint}/api/tags` → `models[].name`
- `OpenAiChatCompletions`, `OpenAiResponses`: `GET {endpoint}/v1/models` → `data[].id`

endpoint가 이미 `/v1`로 끝나면 중복으로 `/v1/v1/models`가 되지 않도록 `/models`만 붙입니다. 목록이 비어 있거나 서버가 모델 목록을 제공하지 않으면 모델명을 직접 입력할 수 있습니다.

## Capability probe

앱 설정의 **연결 테스트**는 메일 내용이 아닌 synthetic probe를 보냅니다. v0.13.0부터는 단순 JSON object ping이 아니라 MailWhere가 실제로 쓰는 follow-up single-item, follow-up batch, waiting-closure request shape을 검증합니다.

- 성공: endpoint/model/provider/structured-output/thinking-control 조합이 single-item, batch, waiting-closure 응답 계약을 모두 통과함
- `not-configured`: provider/model/endpoint가 비어 있거나 LLM이 꺼져 있음
- `invalid-json`: 응답이 JSON object가 아님
- `timeout`: 설정된 timeout 안에 응답하지 않음
- `http-error`: endpoint 연결/HTTP 오류
- `schema-mismatch`: 필수 field, id, item count, disposition/kind 계약이 맞지 않음
- `thinking-leakage`: `<think>` 같은 reasoning text가 final content에 남음
- `truncated`: finish reason이나 parser 결과가 출력 잘림을 가리킴
- `closure-analysis-shape`: waiting-closure 응답 계약을 통과하지 못함
- `reasoning-incomplete`: reasoning metadata나 Responses incomplete 상태가 final JSON 계약을 방해함

성공한 probe는 request-contract version, provider, normalized endpoint, model, thinking-control mode, structured-output mode, temperature, max-output-token policy, batch-size policy fingerprint와 함께 저장됩니다. 이 값 중 하나라도 바뀌면 저장된 proof는 stale이 되고, LLM 분석은 새 probe가 통과할 때까지 fail-closed로 거부됩니다. v0.13.2의 sampling contract 변경도 기존 proof를 자동 폐기합니다.

LLM-backed waiting-closure 판단도 같은 현재 proof가 있을 때만 endpoint를 호출합니다. 이 proof는 single-item, batch, waiting-closure synthetic 요청이 모두 통과할 때만 저장됩니다. proof가 없거나 stale이면 closure 판단은 rule-based fallback에 머뭅니다.

로컬 30B 이상 모델은 첫 호출이나 긴 메일에서 30초를 넘길 수 있으므로 기본 timeout은 90초입니다. 필요하면 설정에서 30초/1분/1분 30초/3분 중 선택합니다. 사용자가 [스캔 중지]를 누른 cancellation은 timeout과 구분되어 즉시 스캔을 멈춥니다.

## 보안 원칙

- prompt와 raw mail body는 저장하지 않습니다.
- SQLite에는 source hash, 짧은 제목/사유/근거 snippet을 저장합니다. Outlook 원본 메일 열기와 업무보드 한 줄 표기를 위해 새 항목에는 로컬 source id, 보낸 사람 표시명, 수신 시각, 수신/참조 역할도 저장할 수 있으며, source-derived data 삭제/Not-a-task 처리/AI 분석 실패 항목 정리 시 함께 제거하거나 비식별화합니다. CLI/search/list/export normal output은 source id/hash나 StoreID/EntryID를 내보내지 않고, explicit Skill launch에는 opaque `open_source_token`만 사용합니다. Internal WPF/Outlook adapter code may retain raw locators inside the trusted local process/database boundary.
- 외부 네트워크 LLM은 기본 사용 시나리오가 아닙니다. 승인된 보안 정책이 허용할 때만 켭니다.
- LLM JSON 파싱이 실패하면 선택한 `LlmFallbackPolicy`에 따라 확인 필요에 남기거나 rule-based analyzer로 fallback합니다.

## Upstream references

- Qwen3.8-27B model card and thinking/non-thinking sampling profiles: <https://huggingface.co/Qwen/Qwen3.8-27B>
- vLLM Qwen/Qwen3.8-27B recipe: <https://recipes.vllm.ai/Qwen/Qwen3.8-27B>
- vLLM reasoning outputs and `enable_thinking`/`reasoning_effort` behavior: <https://docs.vllm.ai/en/latest/features/reasoning_outputs/>
