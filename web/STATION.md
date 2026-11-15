# Automatic Station replies (Web 0.14)

## Delivery invocation compatibility (0.14.1)

Alice can retain the full `Скажи навыку Глубокий разум доставка <number>`
invocation in `original_utterance`. Both this form and the bare delivery command
now use the authenticated one-use delivery path. Previously the full form could
be mistaken for a new question, cancelling the real delivery and invoking Qwen.
Malformed or stale protocol requests cannot create a model job. Ownership,
nonce expiry, active-conversation checks and duplicate-event replay still apply.
Ordinary questions about delivery remain normal dialogue.

Historical misclassified protocol records remain in the archive, but are
excluded from future model context. Diagnostic transport status contains only
status flags and error classes, never commands, numbers, tokens or reply text.

The normal Alice webhook has a 4.5-second response deadline. It cannot push a
later reply. This optional adapter uses the community-documented local Station
protocol to invoke the same private skill with a one-use delivery number. The
skill retrieves the saved Qwen answer; delivery never calls the model again.

References:
- https://yandex.ru/dev/dialogs/alice/doc/ru/wait-response
- https://github.com/AlexxIT/YandexStation
- https://websocket-client.readthedocs.io/en/latest/examples.html

## Account and device

Web 0.13.3 uses Yandex's documented device-code OAuth flow (`station_oauth.py`).
Web shows a short user code and Yandex's verification page; the user signs in
and approves access there. The music client supplies the Glagol access needed
by the local Station protocol. The approval page may name Yandex Music.

The private device code remains in process memory. Polling respects the returned
interval, expiry and rate limits; refreshing invalidates the previous local
attempt. Only the music/Glagol token is stored in `station-output.json` (0600),
after verifying access to the configured Station. The token is sensitive and
never appears in the Web API. Only public user-code metadata is shown, through
authenticated Web routes. The account must own the Station.

Reference: https://yandex.ru/dev/id/doc/ru/codes/screen-code-oauth

The older Passport QR helper remains available for diagnostics, but Web no
longer uses it: fresh QR codes were rejected even with the correct Yandex app.
Neither method reads browser profiles or requests passwords through DeepBrain.

## Quiet, faster replies (0.14.0)

Normal automatic waiting responses use `response.tts: "sil <[100]>"` with
`end_session: true`: a documented 100 ms pause replaces spoken acknowledgment.
The screen can still show a readable status. Pairing, errors and actual answers
are not muted. Explicitly disabled or unavailable automatic delivery retains
the existing manual-retrieval instructions.

Reference: https://yandex.ru/dev/dialogs/alice/doc/ru/speech-tuning

The automatic webhook waits at most 150 ms for an immediate answer. The old
eight-second delivery floor is replaced by a 500 ms response grace period;
the completed-answer and idle-Station checks still apply. Busy devices are
checked again after one second; unavailable devices retain the four-second
backoff. Uncertain sends are never retried.

Voice calls keep desktop Qwen loaded for 30 minutes after use. Model, context,
complete stored history, output limit and fallback to server Qwen are retained.
The server model keeps its existing five-minute policy; Web calls use ten.
No background question is generated to warm the model.

Output targets one configured Max, using its device ID and pinned local TLS
certificate. A certificate change fails closed. Only a fixed skill invocation
and a numeric delivery code can be sent; model output is never a device command.
The dependency lives in `/home/ivan/deepbrain-runtime/station-output-deps`, apart
from the system Python installation. The installed wheel hash/version are saved
there. No Home Assistant instance is required.

## Delivery and persistence

`station_deliveries` is an additive table in `alice.sqlite3`. Existing voice
jobs, conversation rows, and cognitive data are retained. Activation never
enqueues previous history. New delayed requests receive a waiting message and
close the skill while Qwen generates. The adapter waits for an idle Station,
then invokes the skill. Long replies use consecutive chunks; the last chunk
keeps the skill open for the next question.

Callbacks require the existing secret endpoint, skill ID and paired Yandex
account, plus an active one-use delivery number. Cursor advancement, delivery
state and callback replay record commit together. Manual retrieval cancels an
unsent automatic delivery. Changing the conversation, explicit skill exit,
disabling the feature, or expiration cancels remaining output. An uncertain send
is never replayed automatically; the answer remains available in Web and through
manual voice retrieval. Queued unsent jobs can resume after a Web restart;
in-flight jobs become uncertain. A Station acknowledgment is not proof the user
heard a reply, so live verification also checks the webhook and speech states.
Max firmware can report `NONE` while speaking (also documented by the upstream
integration in issue #740). After sending, both `SPEAKING` and `NONE` count as
speech start; completion still requires the subsequent `IDLE` or `LISTENING`
transition and the authenticated skill callback. Before sending, only `IDLE`
permits delivery, so an already-speaking Station is never interrupted.

Automatic mode can be disabled in Web → Dialogue. This changes only Station
delivery; it does not stop or restart DeepBrain, ML, or Qwen. This is an unofficial
integration and Station firmware changes may require adapter updates.
