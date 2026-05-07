// Web Speech API wrapper. Exposes a small surface to Blazor:
//   speech.startRecognition(dotNetRef, lang)  -> begins continuous-ish recognition
//   speech.stopRecognition()                  -> stops recognition
//   speech.speak(text, lang, rate)            -> uses speechSynthesis to read text
//   speech.cancelSpeech()                     -> cancels in-flight speech
//   speech.playClink()                        -> synthesised cocktail-glass chime
//   speech.isSupported()                      -> boolean
//
// Recognition isn't truly continuous in Chrome; we restart on `onend` while the
// user is still in "listening" mode so the experience feels continuous.
//
// Mic gating: while TTS is speaking, we suspend the recogniser so the
// synthesiser doesn't trigger itself through the mic. The recogniser resumes
// on `utter.onend` / `onerror` (or when the caller cancels speech).

(() => {
    const Recognition =
        window.SpeechRecognition || window.webkitSpeechRecognition;

    let recognizer = null;
    let dotNetRef = null;
    let lastLang = "en-US";
    let listening = false;
    let suspendedForTts = false;
    let audioCtx = null;

    function isSupported() {
        return !!Recognition && !!window.speechSynthesis;
    }

    function buildRecognizer(lang) {
        const r = new Recognition();
        r.lang = lang || "en-US";
        r.continuous = true;
        r.interimResults = false;
        r.maxAlternatives = 1;

        r.onresult = (event) => {
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const result = event.results[i];
                if (result.isFinal && dotNetRef) {
                    const transcript = result[0].transcript.trim();
                    dotNetRef.invokeMethodAsync("OnSpeechHeard", transcript);
                }
            }
        };

        r.onerror = (event) => {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("OnSpeechError", event.error || "unknown");
            }
            if (event.error === "not-allowed" || event.error === "service-not-allowed") {
                listening = false;
            }
        };

        r.onend = () => {
            // Auto-restart while we're still meant to be listening, unless
            // we paused on purpose for TTS.
            if (listening && !suspendedForTts && recognizer) {
                try { recognizer.start(); } catch { /* swallow re-start race */ }
            } else if (!listening && dotNetRef) {
                dotNetRef.invokeMethodAsync("OnSpeechStopped");
            }
        };

        return r;
    }

    function startRecognition(ref, lang) {
        if (!Recognition) return false;
        dotNetRef = ref;
        lastLang = lang || "en-US";
        listening = true;
        suspendedForTts = false;

        recognizer = buildRecognizer(lastLang);
        try {
            recognizer.start();
            return true;
        } catch {
            listening = false;
            return false;
        }
    }

    function stopRecognition() {
        listening = false;
        suspendedForTts = false;
        if (recognizer) {
            try { recognizer.stop(); } catch { /* noop */ }
            recognizer = null;
        }
        dotNetRef = null;
    }

    function pauseForTts() {
        if (!listening || !recognizer) return;
        suspendedForTts = true;
        try { recognizer.stop(); } catch { /* noop */ }
    }

    function resumeAfterTts() {
        if (!listening) {
            suspendedForTts = false;
            return;
        }
        suspendedForTts = false;
        if (!recognizer) {
            recognizer = buildRecognizer(lastLang);
        }
        try { recognizer.start(); } catch { /* may already be running */ }
    }

    function speak(text, lang, rate) {
        if (!window.speechSynthesis || !text) return;
        const utter = new SpeechSynthesisUtterance(text);
        utter.lang = lang || "en-US";
        utter.rate = rate || 1.0;
        utter.onstart = pauseForTts;
        utter.onend = resumeAfterTts;
        utter.onerror = resumeAfterTts;
        window.speechSynthesis.cancel();
        window.speechSynthesis.speak(utter);
    }

    function cancelSpeech() {
        if (window.speechSynthesis) {
            window.speechSynthesis.cancel();
        }
        // `cancel()` doesn't reliably fire utter.onend across browsers.
        resumeAfterTts();
    }

    function getAudioCtx() {
        if (!audioCtx) {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return null;
            audioCtx = new Ctx();
        }
        return audioCtx;
    }

    // Cocktail-themed chime: two short, bright glass-clink tones with
    // exponential decay. Synthesised via Web Audio — no audio asset needed.
    function playClink() {
        const ctx = getAudioCtx();
        if (!ctx) return;
        if (ctx.state === "suspended") {
            try { ctx.resume(); } catch { /* noop */ }
        }
        const now = ctx.currentTime;
        clinkAt(ctx, now, 1760);
        clinkAt(ctx, now + 0.18, 2349);
    }

    function clinkAt(ctx, when, freq) {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = "sine";
        osc.frequency.setValueAtTime(freq, when);
        gain.gain.setValueAtTime(0.0001, when);
        gain.gain.exponentialRampToValueAtTime(0.35, when + 0.005);
        gain.gain.exponentialRampToValueAtTime(0.0001, when + 0.6);
        osc.connect(gain).connect(ctx.destination);
        osc.start(when);
        osc.stop(when + 0.65);
    }

    window.speech = {
        isSupported,
        startRecognition,
        stopRecognition,
        speak,
        cancelSpeech,
        playClink,
    };
})();
