    // Called from Blazor (IJSRuntime). Theme and language live on <html> so CSS tokens and RTL switch.
    window.mindlift = {
        // Fades colours between dark and light instead of flipping at once. The fade class is only on while the switch happens.
        setTheme: function (theme) {
            var root = document.documentElement;
            if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
                root.classList.add('ml-theme-fade');
                clearTimeout(window.mindlift._fade);
                window.mindlift._fade = setTimeout(function () { root.classList.remove('ml-theme-fade'); }, 450);
            }
            root.setAttribute('data-theme', theme);
        },
        // Sets the page language and direction without saving a choice. Used for a language that comes from the profile.
        applyLanguage: function (lang) {
            document.documentElement.setAttribute('lang', lang);
            document.documentElement.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
        },
        setLanguage: function (lang) {
            document.documentElement.setAttribute('lang', lang);
            document.documentElement.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
            localStorage.setItem('ml.lang', lang);
        },
        // Saves bytes the app already has (an export) as a file download. The data arrives as base64 text.
        downloadFile: function (fileName, contentType, base64) {
            var bytes = Uint8Array.from(atob(base64), function (c) { return c.charCodeAt(0); });
            var url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
            var link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        }
    };

    // Firefox only starts a drag when data is set on dragstart, and Blazor cannot set it from C#.
    document.addEventListener('dragstart', function (e) {
        if (e.target && e.target.closest && e.target.closest('[data-kanban-card]')) {
            e.dataTransfer.setData('text/plain', 'card');
            e.dataTransfer.effectAllowed = 'move';
        }
    });
