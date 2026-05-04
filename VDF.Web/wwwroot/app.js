window.vdf = {
    copyToClipboard: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
        } catch {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
        }
    },

    getTheme: function () {
        var saved = localStorage.getItem('vdf-theme');
        if (saved === 'light' || saved === 'dark') return saved;
        return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    },

    setTheme: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('vdf-theme', theme);
    },

    initTheme: function () {
        var theme = this.getTheme();
        document.documentElement.setAttribute('data-theme', theme);
    },

    getCompareMode: function () {
        var saved = localStorage.getItem('vdf-compare-mode');
        return saved || '';
    },

    setCompareMode: function (mode) {
        localStorage.setItem('vdf-compare-mode', mode);
    },

    scrollToGroup: function (index) {
        var groups = document.querySelectorAll('.dup-group');
        if (groups[index]) {
            groups[index].scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    },

    // Lazy-load thumbnails when they enter the viewport
    _observer: null,
    initLazyThumbs: function () {
        // Only create observer once; just observe new unloaded images
        if (!this._observer) {
            this._observer = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        var img = entry.target;
                        var src = img.getAttribute('data-src');
                        if (src) {
                            img.src = src;
                            img.removeAttribute('data-src');
                            img.addEventListener('load', function () {
                                img.classList.add('loaded');
                                var skel = img.parentElement.querySelector('.thumb-skeleton');
                                if (skel) skel.style.display = 'none';
                            }, { once: true });
                        }
                        vdf._observer.unobserve(img);
                    }
                });
            }, { rootMargin: '200px' });
        }
        // Only observe images that still have data-src (not yet loaded)
        document.querySelectorAll('img[data-src]').forEach(function (img) {
            vdf._observer.observe(img);
        });
    },

    // Swipe comparer — pure JS for fluid pixel-level dragging (no server round-trips)
    initSwipe: function () {
        var el = document.querySelector('.compare-swipe');
        if (!el) return;

        function updatePos(clientX) {
            var rect = el.getBoundingClientRect();
            var pct = ((clientX - rect.left) / rect.width) * 100;
            pct = Math.max(1, Math.min(99, pct));
            el.style.setProperty('--swipe-pos', pct + '%');
        }

        var dragging = false;

        el.addEventListener('mousedown', function (e) {
            dragging = true;
            updatePos(e.clientX);
            e.preventDefault();
        });
        document.addEventListener('mousemove', function (e) {
            if (dragging) updatePos(e.clientX);
        });
        document.addEventListener('mouseup', function () {
            dragging = false;
        });

        // Touch support
        el.addEventListener('touchstart', function (e) {
            dragging = true;
            if (e.touches.length > 0) updatePos(e.touches[0].clientX);
        }, { passive: true });
        document.addEventListener('touchmove', function (e) {
            if (dragging && e.touches.length > 0) updatePos(e.touches[0].clientX);
        }, { passive: true });
        document.addEventListener('touchend', function () {
            dragging = false;
        });

        // Initial position
        el.style.setProperty('--swipe-pos', '50%');
    }
};

// Apply theme immediately to prevent flash of wrong theme
vdf.initTheme();
