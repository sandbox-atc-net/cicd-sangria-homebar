// Sangria UI helpers: theme, scroll, keyboard.
(() => {
    function setTheme(theme) {
        document.documentElement.dataset.theme = theme === 'dark' ? 'dark' : 'light';
    }

    function scrollToBottom(elementId) {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    }

    function bindCmdD() {
        if (window.__sangriaBound) return;
        window.__sangriaBound = true;
        window.addEventListener('keydown', (e) => {
            if ((e.metaKey || e.ctrlKey) && (e.key === 'd' || e.key === 'D')) {
                e.preventDefault();
                const t = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
                setTheme(t);
                try { localStorage.setItem('sangria.theme', t); } catch (e) { }
            }
        });
    }

    bindCmdD();

    window.sangria = {
        setTheme,
        scrollToBottom,
    };
})();
