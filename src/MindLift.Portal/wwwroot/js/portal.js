    window.mindliftPortal = {
        setTheme: function (theme) { document.documentElement.setAttribute('data-theme', theme); },
        setLanguage: function (lang) {
            document.documentElement.setAttribute('lang', lang);
            document.documentElement.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
            localStorage.setItem('ml.portal.lang', lang);
        }
    };
