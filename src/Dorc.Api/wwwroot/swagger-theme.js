(function () {
  var STORAGE_KEY = 'swagger-theme';

  function getSystemPreference() {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  function getTheme() {
    var stored = localStorage.getItem(STORAGE_KEY);
    return stored || getSystemPreference();
  }

  function applyTheme(theme) {
    if (theme === 'dark') {
      document.body.classList.add('swagger-dark');
    } else {
      document.body.classList.remove('swagger-dark');
    }
    updateButton(theme);
  }

  function updateButton(theme) {
    var btn = document.getElementById('swagger-theme-toggle');
    if (btn) {
      btn.textContent = theme === 'dark' ? '\u2600' : '\u263E';
    }
  }

  function createToggle() {
    if (document.getElementById('swagger-theme-toggle')) return;
    var btn = document.createElement('button');
    btn.id = 'swagger-theme-toggle';
    btn.className = 'swagger-theme-toggle';
    btn.title = 'Toggle dark mode';
    btn.addEventListener('click', function () {
      var current = document.body.classList.contains('swagger-dark') ? 'dark' : 'light';
      var next = current === 'dark' ? 'light' : 'dark';
      localStorage.setItem(STORAGE_KEY, next);
      applyTheme(next);
    });
    // Insert into the topbar next to the definition dropdown
    var topbar = document.querySelector('.swagger-ui .topbar-wrapper');
    if (topbar) {
      topbar.style.display = 'flex';
      topbar.style.alignItems = 'center';
      topbar.appendChild(btn);
    } else {
      // Topbar not ready yet, observe until it appears
      var obs = new MutationObserver(function () {
        var tb = document.querySelector('.swagger-ui .topbar-wrapper');
        if (tb && !document.getElementById('swagger-theme-toggle')) {
          tb.style.display = 'flex';
          tb.style.alignItems = 'center';
          tb.appendChild(btn);
          updateButton(getTheme());
          obs.disconnect();
        }
      });
      obs.observe(document.body, { childList: true, subtree: true });
    }
  }

  function init() {
    createToggle();
    applyTheme(getTheme());

    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
      if (!localStorage.getItem(STORAGE_KEY)) {
        applyTheme(e.matches ? 'dark' : 'light');
      }
    });
  }

  // Use window.onload to guarantee body and Swagger UI are ready
  window.addEventListener('load', init);
})();
