window.vdf = {
    copyToClipboard: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
        } catch {
            // Fallback for non-HTTPS contexts
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
        }
    }
};
