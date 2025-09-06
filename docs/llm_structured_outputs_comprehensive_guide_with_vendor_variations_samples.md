# LLM Structured Outputs — Comprehensive Guide

**Goal:** A single, practical reference for designing, prompting, validating, and testing structured outputs from Large Language Models (LLMs), with concrete, copy‑pasteable examples and vendor‑specific notes.

---

## 1) Why Structured Outputs?

- **Machine-readability**: Parse results deterministically (JSON, XML, CSV, etc.).
- **Safety & robustness**: Guard against prompt drift, hallucinated keys, and malformed values.
- **Automation**: Power pipelines, agents, and tools with reliable contracts.
- **Evaluation**: Easy to diff, schema‑validate, and regression test.

---

## 2) Common Output Formats

### 2.1 JSON (most common)
**Pros:** ubiquitous, parseable, tooling-rich.  
**Cons:** no comments, strict syntax, ambiguous types (e.g., date vs string).

**Pitfalls & fixes:**
- Trailing commas, unescaped quotes → **enforce strict mode** and **validate** with JSON Schema.  
- Hallucinated keys → **additionalProperties: false** where possible.  
- Numbers vs strings → **type constraints** and post-parse checks.

### 2.2 JSON Lines (NDJSON)
Great for streaming batches: one JSON object per line.

### 2.3 XML
**Pros:** schema-able (XSD), tagging can reduce drift.  
**Cons:** verbosity; strict escaping.

### 2.4 YAML
Readable for humans, but **not recommended** for strict machine parsing (indentation quirks). Convert to JSON before use.

### 2.5 CSV / TSV
Tabular, compact. Risk of commas/newlines inside fields; require quoting discipline.

---

## 3) Schema & Validation Options

### 3.1 JSON Schema (recommended)
- Draft 2020‑12 or 2019‑09 are broadly supported.
- Use **$id**, **$defs** for reuse.  
- Prefer **enum**, **const**, **format** (email, uri), **pattern** for regex, **minLength/maxLength**, **minimum/maximum**.  
- Set **additionalProperties: false** to forbid extra keys.

**Minimal example:**
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.com/schemas/user.profile.json",
  "type": "object",
  "additionalProperties": false,
  "required": ["id", "email", "role"],
  "properties": {
    "id": {"type": "string", "minLength": 1},
    "email": {"type": "string", "format": "email"},
    "role": {"type": "string", "enum": ["admin", "member", "viewer"]},
    "bio": {"type": "string", "maxLength": 500}
  }
}
```

### 3.2 Typed Models (Pydantic, TypeBox, Zod, Valibot)
- Use in app code; optionally derive JSON Schema for runtime validation.

### 3.3 XSD / EBNF / Custom Validators
- For XML, define XSD. For compact grammars, EBNF or PEG parsers.

---

## 4) Vendor Landscape — Capabilities & Gotchas

> ⚠️ APIs evolve quickly. Always check current docs before production changes.

### Legend
- **JSON Mode**: Native instruction that **forces** valid JSON.
- **Tools/Functions**: Model picks a function + arguments (a JSON object) you define.
- **Response Format / Schema Binding**: You give a schema; model adheres.
- **Streaming**: Partial tokens or tool calls as they are produced.

### 4.1 OpenAI (incl. Azure OpenAI)
- **Tools/Function Calling:** Define functions with JSON schemas; model returns a `tool_call` with `arguments` JSON. Good for reliability.
- **JSON Mode / Response Format:** "JSON‑only" (aka `response_format: { type: "json_object" }`) or structured schemas that constrain output.  
- **Multi‑tool:** Model can choose among multiple tools; generally single tool per step.  
- **Streaming:** Text and tool‑call deltas.
- **Tips:** Prefer **function calling** for API‑like interactions; include enums, min/max, and `additionalProperties: false`. Use **system** instructions to reject non‑JSON.

### 4.2 Anthropic (Claude)
- **Tool Use:** Similar to function calling; model emits `tool_use` blocks with JSON args.  
- **XML‑style Constraints:** Claude responds well to tagged XML or JSON with strong instructions.  
- **System prompts** can include *hard* JSON rules.  
- **Streaming:** Tokens + tool-use events.  
- **Tips:** Provide **exemplars** and **counter‑examples**. Use **"must output ONLY JSON"** guardrails.

### 4.3 Google (Gemini)
- **Function Calling / Tooling** supported.  
- **JSON Schema Binding:** Strong results if you supply schema and few‑shot exemplars.  
- **Safety blocks** can truncate outputs; plan for retries.

### 4.4 Meta Llama (hosted via providers)
- **Structured Outputs:** Varies by host. Many support function calling and JSON mode switches.  
- **Tips:** Be explicit: *"Return only a JSON object…"*; use **retry/repair** on parse failure.

### 4.5 Mistral
- **Function Calling:** via certain endpoints/hosts.  
- **JSON Mode:** Often available as a request flag; reliability improves with few‑shot examples.

### 4.6 Cohere
- **Tool Use / JSON Control:** Supports function/tool patterns; historically good at JSON when given schemas and examples.

### 4.7 AWS Bedrock (Anthropic, Cohere, Mistral, Llama, Amazon models)
- Capabilities mirror the underlying model.  
- **Guardrails**: Bedrock Guardrails can enforce JSON patterns and block out‑of‑policy fields.

### 4.8 OpenRouter & Aggregators
- Features mirror upstream models; check per‑model docs for JSON mode, tool‑use, streaming.

---

## 5) Design Patterns

### 5.1 Three Core Strategies
1) **JSON‑Only Output** (simple extract/transform).  
2) **Function/Tool Calling** (model chooses a function + args → you execute).  
3) **Schema‑Bound Output** (JSON Schema or type descriptor embedded in prompt).

### 5.2 Reliability Layers (Recommended)
- **Layer 0:** Clear schema + few‑shot examples.  
- **Layer 1:** **Strict mode** (JSON mode / response_format).  
- **Layer 2:** Runtime **schema validation**; on failure, **auto‑repair** with the error message.  
- **Layer 3:** **Safety nets**: timeouts, token budget checks, truncation detection, and retries.  
- **Layer 4:** **Consistency checks**: enums, checksums, cross‑field rules.

### 5.3 Repair Loop Snippet
Provide the model the **exact** validator error to self‑correct.

**Repair prompt (system or user):**
```
You produced invalid JSON for the required schema. Here is the validator error:

<error>
${validatorError}
</error>

Return only a corrected JSON object that validates.
```

---

## 6) Comprehensive Case Library (with Copy‑Paste Samples)

Each case includes: **Goal**, **Prompt** (system+user), **Expected Structure**, **Sample Output**, and **Validation Notes**.

### Case A — Flat Extraction
**Goal:** Extract simple fields from text.

**Expected JSON Schema:**
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "additionalProperties": false,
  "required": ["title", "author", "published_year"],
  "properties": {
    "title": {"type": "string"},
    "author": {"type": "string"},
    "published_year": {"type": "integer", "minimum": 1400, "maximum": 2100}
  }
}
```

**Prompt:**
- **System:** *You are a JSON machine. Output ONLY a JSON object that validates against the provided schema.*
- **User:**
  ```
  Text: "In 1865, Lewis Carroll published Alice's Adventures in Wonderland."
  Produce: {title, author, published_year}
  ```

**Sample Output:**
```json
{"title":"Alice's Adventures in Wonderland","author":"Lewis Carroll","published_year":1865}
```

**Validation Notes:** Reject if year ∉ [1400,2100].

---

### Case B — Nested Records with Arrays
**Goal:** Parse a product with variants.

**Schema:**
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "additionalProperties": false,
  "required": ["sku", "name", "variants"],
  "properties": {
    "sku": {"type": "string"},
    "name": {"type": "string"},
    "variants": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["id", "price", "currency", "in_stock"],
        "properties": {
          "id": {"type": "string"},
          "price": {"type": "number", "minimum": 0},
          "currency": {"type": "string", "pattern": "^[A-Z]{3}$"},
          "in_stock": {"type": "boolean"}
        }
      }
    }
  }
}
```

**Prompt (User):**
```
Create a sample product for demo purposes (fictional names ok). 2–4 variants.
```

**Sample Output:**
```json
{
  "sku": "DEMO-001",
  "name": "Aurora Mug",
  "variants": [
    {"id":"DEMO-001-BLK","price":12.99,"currency":"USD","in_stock":true},
    {"id":"DEMO-001-WHT","price":12.99,"currency":"USD","in_stock":false}
  ]
}
```

---

### Case C — Enum, Union, and Optional Fields
**Goal:** Robust handling of categorical outputs.

**Schema:**
```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["kind"],
  "properties": {
    "kind": {"type": "string", "enum": ["person","company","nonprofit"]},
    "person": {
      "type": "object",
      "additionalProperties": false,
      "required": ["name","age"],
      "properties": {"name": {"type":"string"},"age": {"type":"integer","minimum":0}}
    },
    "company": {"type":"object","properties":{"name":{"type":"string"},"employees":{"type":"integer","minimum":1}}},
    "nonprofit": {"type":"object","properties":{"name":{"type":"string"},"cause":{"type":"string"}}}
  },
  "oneOf": [
    {"required":["person"]},
    {"required":["company"]},
    {"required":["nonprofit"]}
  ]
}
```

**Prompt (User):**
```
From the text, classify the entity and fill the matching object. Text: "Acme Corp employs 120 people in robotics."
```

**Sample Output:**
```json
{"kind":"company","company":{"name":"Acme Corp","employees":120}}
```

**Validation Notes:** Ensure exactly one of the option-objects is present.

---

### Case D — Tool/Function Calling (General Pattern)
**Goal:** Let the model choose an action and provide structured args.

**Tool Schema (example):**
```json
{
  "name": "create_calendar_event",
  "description": "Create a calendar event",
  "parameters": {
    "type": "object",
    "additionalProperties": false,
    "required": ["title","start","end"],
    "properties": {
      "title": {"type": "string"},
      "start": {"type": "string", "format": "date-time"},
      "end": {"type": "string", "format": "date-time"},
      "location": {"type": "string"}
    }
  }
}
```

**User:**
```
Set a 30‑minute "1:1 with Taylor" tomorrow at 10am in Room B.
```

**Model Tool Call (sample):**
```json
{
  "tool_call": {
    "name": "create_calendar_event",
    "arguments": {
      "title": "1:1 with Taylor",
      "start": "2025-09-06T10:00:00-07:00",
      "end": "2025-09-06T10:30:00-07:00",
      "location": "Room B"
    }
  }
}
```

**Notes:** Always validate times; confirm daylight saving; handle locale.

---

### Case E — Strict JSON Mode (No Tools)
**Goal:** Force pure JSON output for ingestion.

**System Prompt:**
```
You are a strict JSON generator. Respond with ONLY a JSON object that matches the schema. Do not include explanations, code fences, or extra keys.
```

**User:**
```
Summarize this article into fields: {title, bullets[3-5], url}. Article: <paste>.
```

**Sample Output:**
```json
{
  "title": "The State of Edge AI in 2025",
  "bullets": [
    "Edge TPUs reduce latency and cost",
    "On‑device privacy enables new healthcare cases",
    "Frameworks converge around ONNX"
  ],
  "url": "https://example.com/edge-ai-2025"
}
```

---

### Case F — XML‑Tagged Output
**Goal:** Tighter control with tags.

**System:**
```
Return ONLY an <result> XML document following this structure:
<result>
  <title/>
  <highlights>
    <item/>
  </highlights>
</result>
```

**User:** "Make a title + 2 highlights for a talk on RAG."

**Sample Output:**
```xml
<result>
  <title>Retrieval‑Augmented Generation, Demystified</title>
  <highlights>
    <item>Indexing strategies that actually matter</item>
    <item>Failure modes and evaluation</item>
  </highlights>
</result>
```

---

### Case G — Streaming Lists (NDJSON)
**Goal:** Emit items incrementally for early consumption.

**User:**
```
Generate 5 synthetic customer profiles, one per line (NDJSON).
```

**Sample Output:**
```
{"id":"u_001","tier":"pro"}
{"id":"u_002","tier":"free"}
{"id":"u_003","tier":"pro"}
{"id":"u_004","tier":"free"}
{"id":"u_005","tier":"pro"}
```

---

### Case H — Robust Dates, Numbers, and Units
**Goal:** Enforce ISO dates, decimals, and units.

**Schema (fragment):**
```json
{
  "type":"object",
  "properties":{
    "date_iso": {"type":"string","pattern":"^\\d{4}-\\d{2}-\\d{2}$"},
    "price": {"type":"string","pattern":"^\\d+\\.\\d{2} [A-Z]{3}$"},
    "length_cm": {"type":"number","minimum":0}
  },
  "required":["date_iso","price","length_cm"],
  "additionalProperties": false
}
```

**Sample Output:**
```json
{"date_iso":"2025-09-05","price":"129.00 USD","length_cm":18.5}
```

---

### Case I — Multi‑Step Agent Plans (Tool Sequence)
**Goal:** Plan → call tools → produce final JSON.

**Expected Output:**
```json
{
  "plan": [
    {"step":1,"action":"search","query":"..."},
    {"step":2,"action":"fetch","url":"..."}
  ],
  "final": {"answer":"..."}
}
```

**Tip:** Keep **reflective chain** in a separate key to avoid mixing with `final`.

---

### Case J — Classification with Confidence & Rationale
**Schema:**
```json
{
  "type":"object",
  "properties":{
    "label":{"type":"string","enum":["positive","neutral","negative"]},
    "confidence":{"type":"number","minimum":0,"maximum":1},
    "rationale":{"type":"string","maxLength":300}
  },
  "required":["label","confidence"],
  "additionalProperties": false
}
```

**Sample Output:**
```json
{"label":"positive","confidence":0.83,"rationale":"Mentions prompt support and fast shipping."}
```

---

### Case K — Robust Extraction from Messy Text
**Goal:** Handle emojis, quotes, HTML.

**Prompt Tip:** Pre‑clean text or instruct: *"Ignore HTML tags; unescape entities; keep emojis in Unicode"*.

**Sample Output:**
```json
{
  "name":"Fran & Co.",
  "caption":"We ❤️ fast builds!",
  "html_stripped":true
}
```

---

### Case L — Redaction & PII Masking
**Goal:** Output structured fields with masked PII.

**Schema add‑on:** `"format": "email"` and `"pattern": "^.+@example\\.com$"` after mapping.

**Sample Output:**
```json
{"email":"u123@example.com","masked":true}
```

---

### Case M — Program Synthesis (JSON → Code)
**Goal:** Emit code in JSON string fields to avoid markdown fences.

**Schema:**
```json
{
  "type":"object",
  "properties":{ "filename": {"type":"string"}, "code": {"type":"string"} },
  "required":["filename","code"],
  "additionalProperties": false
}
```

**Sample Output:**
```json
{
  "filename":"slugify.ts",
  "code":"export const slugify=(s)=>s.toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,'');"
}
```

---

### Case N — Conversational State Machines
**Goal:** Force dialog states.

**Schema:**
```json
{
  "type":"object",
  "additionalProperties": false,
  "properties":{
    "state":{"type":"string","enum":["greeting","collecting","confirming","done"]},
    "slots":{"type":"object","additionalProperties":false,"properties":{
      "name":{"type":"string"},
      "email":{"type":"string","format":"email"}
    }}
  },
  "required":["state","slots"]
}
```

**Sample Output:**
```json
{"state":"collecting","slots":{"name":"Jamie","email":"jamie@example.com"}}
```

---

## 7) Vendor‑Specific Prompt Templates & Samples

> You can drop these into your favorite SDKs. Replace `{…}` placeholders.

### 7.1 OpenAI — JSON‑Only Response Format
**System:**
```
You are a strict JSON generator. Respond with ONLY a JSON object that validates the provided JSON Schema. No prose.
```
**User:**
```
Schema:
{JSON_SCHEMA_HERE}
Task:
{INSTRUCTIONS}
```
**Expected Behavior:** Model returns a single JSON object; validate client‑side.

### 7.2 OpenAI — Function/Tool Calling
**Client setup (pseudo):**
```json
{
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "create_ticket",
        "description": "Create a support ticket",
        "parameters": {
          "type":"object","additionalProperties":false,
          "required":["title","priority"],
          "properties":{
            "title":{"type":"string"},
            "priority":{"type":"string","enum":["low","medium","high"]}
          }
        }
      }
    }
  ],
  "tool_choice": "auto"
}
```
**Model output (sample):**
```json
{"tool_call":{"name":"create_ticket","arguments":{"title":"Login fails on Safari","priority":"high"}}}
```

### 7.3 Anthropic — Tool Use JSON
**System:**
```
Return JSON only. If a tool is needed, emit a tool_use with valid JSON args that respect the schema.
```
**Tool schema:** same pattern as above.  
**Sample:**
```json
{"tool_use":{"name":"send_email","input":{"to":"ops@example.com","subject":"Alert","body":"Service down"}}}
```

### 7.4 Gemini — Schema Binding + Safety
**Prompt tip:**
```
Follow this JSON schema strictly. If any required field is missing, ask for it via a single JSON error object: {"error":"...","missing":["field"]}
```
**Sample output:**
```json
{"error":"Missing required fields","missing":["start","end"]}
```

### 7.5 Llama / Mistral / Cohere — JSON Mode
**Prompt tip:**
```
Return only valid JSON. Do not include markdown fences, comments, or trailing commas. Use these enums: …
```

---

## 8) Edge Cases & Hardening Checklist

- **Determinism:** Fix temperature (e.g., 0–0.3) for strict tasks.
- **Length:** Set max tokens; detect truncation (unterminated braces, arrays).
- **Unicode:** Ensure UTF‑8; test emojis, smart quotes, CJK.
- **Escaping:** Watch for embedded JSON in strings → double‑escape `"` and `\` where needed.
- **Nullability:** Decide between `null` vs missing keys; reflect in schema (`nullable` or union with `null`).
- **Numbers:** Prevent 64‑bit overflow; use strings for bigints, money as decimal strings.
- **Dates/Times:** Use ISO 8601 with timezone; forbid ambiguous formats.
- **Locales:** Decimal separators (`,` vs `.`); currency codes (ISO 4217).
- **Security:** Never execute returned code blindly; sanitize HTML; avoid prompt injection by stripping user‑controlled system content.
- **Safety Filters:** Some vendors redact or truncate—implement retries.
- **Streaming:** Handle partial JSON safely (json‑repair or buffer until valid end token).
- **Versioning:** `$id` with semantic version; migrate schemas carefully.

---

## 9) Testing & Evaluation

### 9.1 Golden Tests
- Curate fixtures of input → expected JSON.
- Run schema validation in CI.

### 9.2 Property Tests
- Fuzz prompts (typos, symbols) and assert invariants (no extra keys).

### 9.3 Round‑Trip Tests
- Parse → serialize → compare equality (canonicalize key order, whitespace).

### 9.4 Metrics
- **Validity rate** (parse + schema pass), **repair rate**, **latency**, **token cost**, **accuracy** (task specific).

---

## 10) Minimal Client Patterns (Pseudo‑Code)

### 10.1 Parse + Validate + Repair (TypeScript)
```ts
import { validate } from "jsonschema"; // or ajv

async function callLLM(prompt, schema) {
  const res = await llm(prompt, { response_format: { type: "json_object" } });
  try {
    const obj = JSON.parse(res);
    const ok = validate(obj, schema);
    if (!ok.valid) throw new Error(ok.errors.map(e => e.stack).join("; "));
    return obj;
  } catch (e) {
    const repair = await llm(`You produced invalid JSON. Error: ${e}. Return corrected JSON only.\nSchema:${JSON.stringify(schema)}`);
    return JSON.parse(repair);
  }
}
```

### 10.2 Tool Call Dispatcher (Python)
```py
import json
from jsonschema import validate

def handle_response(resp, tools):
  if "tool_call" in resp:
    call = resp["tool_call"]
    fn = tools[call["name"]]
    validate(instance=call["arguments"], schema=fn.schema)
    return fn(**call["arguments"])
  else:
    return resp  # plain JSON
```

---

## 11) Reusable Prompt Snippets

**Strict JSON guard:**
```
Output ONLY a JSON object. No prose, no code fences. If unsure, emit {"error":"…"}.
```

**Disallow extra keys:**
```
Do not invent fields. Allowed keys are exactly: ...
```

**Enum clarity:**
```
Use one of: ["low","medium","high"]. Anything else is invalid.
```

**Locale/timezone:**
```
Use ISO 8601 with timezone offset, e.g., 2025-09-05T10:00:00-07:00
```

---

## 12) Ready‑Made Schemas (Grab‑Bag)

- **Contact:** name, email (format), phone (E.164), roles (enum).  
- **Task:** id (uuid), title, priority (enum), due_date (date), tags (array, max 10).  
- **Address:** street, city, region (enum), postal_code (pattern by country), country (ISO‑3166‑1 alpha‑2).  
- **Invoice:** id, currency (enum ISO 4217), lines (array, minItems 1), totals (number as string with 2 decimals).  
- **QA Pair:** question, answer, citations (array of uri strings).

*(Add your domain schemas here.)*

---

## 13) Migration & Compatibility Notes

- Switch from free‑form → function calling with a shim layer that adapts old outputs to the new schema.  
- Keep a **compat mode** for old keys until consumers are upgraded.  
- Add **schema version fields** to outputs to avoid silent drift.

---

## 14) Quick Vendor Comparison (Cheat Sheet)

| Vendor | JSON Mode | Tool/Function Calling | Schema Binding | Streaming | Notes |
|---|---:|---:|---:|---:|---|
| OpenAI | ✅ | ✅ | ✅ | ✅ | Reliable with `response_format` + tools |
| Anthropic | ✅ | ✅ | ✅ | ✅ | Strong with XML/JSON; great tool blocks |
| Google (Gemini) | ✅ | ✅ | ✅ | ✅ | Ensure safety doesn’t truncate |
| Meta Llama (hosted) | ⚠️ | ✅ | ⚠️ | ✅ | Varies by host; add repair loop |
| Mistral | ✅ | ✅ | ⚠️ | ✅ | Few‑shot helps adherence |
| Cohere | ✅ | ✅ | ⚠️ | ✅ | Solid classification/extraction |
| AWS Bedrock | n/a | ✅ | Guardrails | ✅ | Guardrails enforce JSON patterns |

> ⚠️ “✅/⚠️” are generalized and can vary by model/host version.

---

## 15) Implementation Checklist

- [ ] Define schema(s) with strictness (no extra keys).  
- [ ] Add exemplars & counter‑examples.  
- [ ] Enable JSON mode / response format.  
- [ ] Validate + repair loop.  
- [ ] Add metrics: validity rate, retries.  
- [ ] Add tests: golden + fuzz.  
- [ ] Document versions & migration path.

---

### Appendix — Copy‑Paste JSON Repair Utility (TS)
```ts
export function tryParseJson(s: string): { ok: true, value: any } | { ok: false, error: string } {
  try { return { ok: true, value: JSON.parse(s) } } catch (e: any) { return { ok: false, error: e.message } }
}
```

---

**End of Guide**

