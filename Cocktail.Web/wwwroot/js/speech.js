// Web Speech API wrapper. Exposes a small surface to Blazor:
//   speech.startRecognition(dotNetRef, lang)  -> begins continuous-ish recognition
//   speech.stopRecognition()                  -> stops recognition
//   speech.speak(text, lang, rate)            -> uses speechSynthesis to read text
//   speech.cancelSpeech()                     -> cancels in-flight speech
//   speech.isSupported()                      -> boolean
//
// Recognition isn't truly continuous in Chrome; we restart on `onend` while the
// user is still in "listening" mode so the experience feels continuous.

(() => {
    const Recognition =
        window.SpeechRecognition || window.webkitSpeechRecognition;

    let recognizer = null;
    let dotNetRef = null;
    let listening = false;

    function isSupported() {
        return !!Recognition && !!window.speechSynthesis;
    }

    function startRecognition(ref, lang) {
        if (!Recognition) return false;
        dotNetRef = ref;
        listening = true;

        recognizer = new Recognition();
        recognizer.lang = lang || "en-US";
        recognizer.continuous = true;
        recognizer.interimResults = false;
        recognizer.maxAlternatives = 1;

        recognizer.onresult = (event) => {
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const result = event.results[i];
                if (result.isFinal && dotNetRef) {
                    const transcript = result[0].transcript.trim();
                    dotNetRef.invokeMethodAsync("OnSpeechHeard", transcript);
                }
            }
        };

        recognizer.onerror = (event) => {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("OnSpeechError", event.error || "unknown");
            }
            // Some errors (no-speech, audio-capture) shouldn't kill the session.
            if (event.error === "not-allowed" || event.error === "service-not-allowed") {
                listening = false;
            }
        };

        recognizer.onend = () => {
            // Auto-restart while we're still meant to be listening.
            if (listening && recognizer) {
                try { recognizer.start(); } catch { /* swallow re-start race */ }
            } else if (dotNetRef) {
                dotNetRef.invokeMethodAsync("OnSpeechStopped");
            }
        };

        try {
            recognizer.start();
            return true;
        } catch (e) {
            listening = false;
            return false;
        }
    }

    function stopRecognition() {
        listening = false;
        if (recognizer) {
            try { recognizer.stop(); } catch { /* noop */ }
            recognizer = null;
        }
        dotNetRef = null;
    }

    function speak(text, lang, rate) {
        if (!window.speechSynthesis || !text) return;
        const utter = new SpeechSynthesisUtterance(text);
        utter.lang = lang || "en-US";
        utter.rate = rate || 1.0;
        window.speechSynthesis.cancel();
        window.speechSynthesis.speak(utter);
    }

    function cancelSpeech() {
        if (window.speechSynthesis) {
            window.speechSynthesis.cancel();
        }
    }

    window.speech = {
        isSupported,
        startRecognition,
        stopRecognition,
        speak,
        cancelSpeech,
    };
})();
