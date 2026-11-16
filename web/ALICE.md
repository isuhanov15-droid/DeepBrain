# Яндекс Станция: «Глубокий разум»

> Историческое описание Web 0.12. В Web 0.17.1 возвращено название «Глубокий разум»; автоматический ответ без голосового ожидания описан в [STATION.md](STATION.md). При настроенной автоматической доставке спрашивать «Готово?» не нужно. Запуск: «Алиса, запусти навык Глубокий разум». В активном разговоре отдельное обращение не нужно.

Web 0.12 adds a separate loopback listener on 127.0.0.1:8789. Publish ONLY this
listener through HTTPS (e.g. Tailscale Funnel). Never publish Studio 8788,
DeepBrain 8787, or Ollama. Requests require a random secret URL, the configured
skill ID and a Yandex account paired by an expiring one-time code. Keep the skill
private, under the same Yandex account as the Station.

## Setup

1. Log the Ninkear server into Tailscale. Enable Funnel and HTTPS for that server.
2. Forward HTTPS to `http://127.0.0.1:8789` with `tailscale funnel --bg 8789`.
3. Create a private Alice skill in https://dialogs.yandex.ru/developer/.
   Name and activation phrase: «Глубокий разум». No screen requirement.
4. Configure from the server terminal:

   `python3 /home/ivan/deepbrain-web-01/setup_alice.py --skill-id YOUR_SKILL_UUID --public-url https://YOUR_SERVER.ts.net`

   Paste the printed secret webhook URL into that private skill. Do not share it
   publicly or put it in screenshots/logs. No separate AI API key is required.
5. In authenticated Studio > Диалог, get a connection code. Start the skill in
   its testing console (authorized user), or publish privately and invoke
   «Алиса, запусти навык Глубокий разум». Say the code digit by digit. The link
   follows the Yandex account across its devices. Anonymous users cannot pair.

## Behavior and persistence

Alice performs cloud speech recognition and speech synthesis. Qwen generates
answers locally; this is not an offline Station. The main computer uses 9B,
with server 4B fallback according to the existing Dialogue configuration.
Alice refuses the OpenAI provider even if the Web provider is changed.

The webhook waits at most one second for generation, leaving network time within
Yandex's 4.5-second total deadline. If still processing, say «Готово?» later.
4B may need several minutes, including contention with the inner voice. Say
«Продолжить» for the next chunk of a long answer; «Повтори» repeats the last chunk.
There is no asynchronous push of speech after a webhook response.
Web 0.12.2 also recognizes the observed Alice transcription «готова» as a request
for the pending/saved answer. It never creates another model call or chat turn.

`alice.sqlite3` persists account binding, expiring pairing challenges, requests,
delivery positions and a seven-day replay cache. `dialogue.sqlite3` remains the
shared authoritative conversation. The same Dialogue instance serializes Web
and voice generation. Unstarted jobs resume on Web restart. An interrupted
model call is reported as interrupted, never silently re-generated; a completed
answer saved before interruption is recovered. Model outputs have no tools or
control commands. Changing the Web conversation before a queued voice job starts
fails that job safely. No core, ML, habits, memory or configuration is rewritten.

There is one voice job at a time. A second question while busy is explicitly
declined; ask it again after hearing the answer. Voice turns have a 256-token
generation cap. Full generated answers remain in Web; only speech is chunked
and stripped of markup. Web 0.12.1 also preserves a partial voice answer from
the desktop model if generation reaches its token limit, and announces that
limit to the listener. Credentials and webhook paths are never logged.

## Maintenance

Back up both SQLite databases and `alice.json` with the rest of Web data.
To disable the endpoint, set `enabled` to false in `alice.json` and disable the
Funnel mapping. Account re-pairing/skill changes must be handled deliberately by
the administrator; the Web UI never exposes a reset or secret endpoint.
Tailscale must remain signed in; follow its key-expiry policy for an unattended
server. Check the URL from outside the home network and test actual voice on the
Station before declaring the integration complete.

Primary references:
- https://yandex.ru/dev/dialogs/alice/doc/ru/request
- https://yandex.ru/dev/dialogs/alice/doc/ru/response
- https://yandex.ru/dev/dialogs/alice/doc/ru/test
- https://tailscale.com/docs/features/tailscale-funnel
