// NexaCommerce shared behaviour (storefront + admin)
(function () {
  // Auto-hide flash messages after a few seconds.
  document.querySelectorAll('[data-autohide]').forEach(function (el) {
    setTimeout(function () { el.style.transition = 'opacity .4s'; el.style.opacity = '0'; setTimeout(function () { el.remove(); }, 450); }, 5000);
  });

  // Confirm dangerous actions: <form data-confirm="Are you sure?">
  document.querySelectorAll('form[data-confirm]').forEach(function (f) {
    f.addEventListener('submit', function (e) { if (!confirm(f.getAttribute('data-confirm'))) e.preventDefault(); });
  });

  // Mobile sidebar toggle in admin
  var toggle = document.getElementById('sidebarToggle');
  if (toggle) toggle.addEventListener('click', function () { document.querySelector('.sidebar').classList.toggle('open'); });

  // Quantity steppers: <div data-stepper><button data-step="-1">…<input>…<button data-step="1">
  document.querySelectorAll('[data-stepper]').forEach(function (wrap) {
    var input = wrap.querySelector('input');
    wrap.querySelectorAll('[data-step]').forEach(function (b) {
      b.addEventListener('click', function () {
        var v = parseInt(input.value || '1', 10) + parseInt(b.getAttribute('data-step'), 10);
        var max = parseInt(input.getAttribute('max') || '9999', 10);
        input.value = Math.max(parseInt(input.getAttribute('min') || '1', 10), Math.min(max, v));
        if (wrap.hasAttribute('data-autosubmit')) wrap.closest('form').submit();
      });
    });
  });
})();

// Shared Chart.js defaults
window.nxChartDefaults = function () {
  if (!window.Chart) return;
  Chart.defaults.font.family = 'Manrope, Segoe UI, sans-serif';
  Chart.defaults.color = '#6E6C8A';
  Chart.defaults.plugins.legend.labels.boxWidth = 12;
  Chart.defaults.plugins.legend.labels.boxHeight = 12;
  Chart.defaults.maintainAspectRatio = false;
};
window.nxPalette = ['#5B3DF5', '#1F9D6B', '#E8A33D', '#2A7FD4', '#E0475B', '#1C7487', '#6B3FA0', '#94600F', '#77748A'];
