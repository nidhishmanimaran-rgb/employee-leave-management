(() => {
  'use strict';

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));

  const prefersReducedMotion = () =>
    window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const getStoredTheme = () => {
    try {
      return localStorage.getItem('elm-theme') || 'dark';
    } catch {
      return 'dark';
    }
  };

  const setTheme = (theme) => {
    document.documentElement.setAttribute('data-theme', theme);
    try {
      localStorage.setItem('elm-theme', theme);
    } catch {
      // ignore storage failures
    }
  };

  const toastRegion = () => $('.toast-region');

  const toast = (message, type = 'info') => {
    const region = toastRegion();
    if (!region) return;

    const node = document.createElement('div');
    node.className = `toast toast-${type}`;
    node.setAttribute('role', 'status');
    node.textContent = message;
    region.appendChild(node);

    window.setTimeout(() => {
      node.classList.add('toast-hide');
      window.setTimeout(() => node.remove(), 250);
    }, 3200);
  };

  const initAlerts = () => {
    $$('.alert-success, .alert-error').forEach((alert) => {
      const isSuccess = alert.classList.contains('alert-success');
      toast(alert.textContent.trim(), isSuccess ? 'success' : 'error');
      alert.remove();
    });
  };

  const initTheme = () => {
    setTheme(getStoredTheme());
    const button = $('[data-theme-toggle]');
    if (!button) return;

    button.addEventListener('click', () => {
      const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
      setTheme(next);
    });
  };

  const initSidebar = () => {
    const sidebar = $('#app-sidebar');
    if (!sidebar) return;

    const backdrop = $('.sidebar-backdrop');
    const toggles = $$('[data-sidebar-toggle]');

    const open = () => {
      sidebar.classList.add('sidebar-open');
      backdrop?.classList.add('active');
      document.body.classList.add('sidebar-lock');
    };

    const close = () => {
      sidebar.classList.remove('sidebar-open');
      backdrop?.classList.remove('active');
      document.body.classList.remove('sidebar-lock');
    };

    toggles.forEach((toggle) => toggle.addEventListener('click', () => {
      sidebar.classList.contains('sidebar-open') ? close() : open();
    }));

    backdrop?.addEventListener('click', close);
    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') close();
    });
  };

  const initCounters = () => {
    const counters = $$('[data-counter]');
    if (!counters.length) return;

    const animate = (el) => {
      const target = parseInt(el.getAttribute('data-counter') || '0', 10);
      if (prefersReducedMotion() || Number.isNaN(target)) {
        el.textContent = String(target);
        return;
      }

      const start = performance.now();
      const duration = 900;

      const step = (now) => {
        const t = Math.min(1, (now - start) / duration);
        const eased = 1 - Math.pow(1 - t, 3);
        el.textContent = String(Math.round(target * eased));
        if (t < 1) requestAnimationFrame(step);
      };

      requestAnimationFrame(step);
    };

    if ('IntersectionObserver' in window) {
      const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            animate(entry.target);
            observer.unobserve(entry.target);
          }
        });
      }, { threshold: 0.25 });

      counters.forEach((el) => observer.observe(el));
      return;
    }

    counters.forEach(animate);
  };

  const initFiltering = () => {
    const input = $('[data-filter-input]');
    const statusFilter = $('[data-status-filter]');
    if (!input && !statusFilter) return;

    const apply = () => {
      const query = (input?.value || '').trim().toLowerCase();
      const status = (statusFilter?.value || '').trim().toLowerCase();

      $$('[data-filter-item]').forEach((item) => {
        const text = item.textContent.toLowerCase();
        const itemStatus = (item.getAttribute('data-status') || '').toLowerCase();
        const matchesQuery = !query || text.includes(query);
        const matchesStatus = !status || itemStatus === status;
        item.style.display = matchesQuery && matchesStatus ? '' : 'none';
      });
    };

    input?.addEventListener('input', apply);
    statusFilter?.addEventListener('change', apply);
    apply();
  };

  const initCalendar = () => {
    const detail = $('[data-calendar-detail]');
    if (!detail) return;

    $$('[data-calendar-date]').forEach((cell) => {
      cell.addEventListener('click', () => {
        detail.innerHTML = `<strong>${cell.getAttribute('data-calendar-date')}</strong><span>${cell.getAttribute('data-calendar-events') || ''}</span>`;
      });
    });
  };

  const initConfirmations = () => {
    $$('[data-confirm]').forEach((form) => {
      form.addEventListener('submit', (event) => {
        if (!window.confirm(form.getAttribute('data-confirm') || 'Are you sure?')) {
          event.preventDefault();
        }
      });
    });
  };

  const sortValue = (cell, type) => {
    const text = (cell?.textContent || '').trim();
    if (type === 'number') return parseFloat(text) || 0;
    if (type === 'date') return Date.parse(text) || 0;
    return text.toLowerCase();
  };

  const initTableSort = () => {
    $$('th[data-sort]').forEach((header) => {
      header.addEventListener('click', () => {
        const table = header.closest('table');
        const body = table?.tBodies?.[0];
        if (!table || !body) return;

        const index = Array.from(header.parentElement.children).indexOf(header);
        const type = header.getAttribute('data-sort') || 'text';
        const ascending = !header.classList.contains('sort-asc');

        header.parentElement.querySelectorAll('[data-sort]').forEach((h) => h.classList.remove('sort-asc', 'sort-desc'));
        header.classList.add(ascending ? 'sort-asc' : 'sort-desc');

        const rows = Array.from(body.rows).sort((a, b) => {
          const left = sortValue(a.cells[index], type);
          const right = sortValue(b.cells[index], type);
          if (left < right) return ascending ? -1 : 1;
          if (left > right) return ascending ? 1 : -1;
          return 0;
        });

        rows.forEach((row) => body.appendChild(row));
      });
    });
  };

  const exportCsv = (table) => {
    const headers = Array.from(table.tHead?.rows?.[0]?.cells || []).map((cell) => cell.textContent.trim());
    const rows = Array.from(table.tBodies?.[0]?.rows || []).map((row) =>
      Array.from(row.cells).map((cell) => `"${cell.textContent.trim().replace(/"/g, '""')}"`).join(','));
    return [headers.join(','), ...rows].join('\r\n');
  };

  const download = (content, fileName, mimeType) => {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  const initExport = () => {
    $$('[data-export]').forEach((button) => {
      button.addEventListener('click', () => {
        const target = $(button.getAttribute('data-export-target') || '');
        if (!target) return;

        const stamp = new Date().toISOString().slice(0, 10);
        const format = button.getAttribute('data-export');

        if (format === 'csv' || format === 'excel') {
          download(exportCsv(target), `leave-requests-${stamp}.${format === 'excel' ? 'xls' : 'csv'}`, 'text/csv;charset=utf-8;');
          toast(`Exported ${format.toUpperCase()}.`, 'success');
          return;
        }

        if (format === 'pdf') {
          const win = window.open('', '_blank', 'width=1100,height=800');
          if (!win) return;
          win.document.write(`<!doctype html><html><head><title>Export</title><style>body{font-family:Arial;padding:24px}table{width:100%;border-collapse:collapse}th,td{border:1px solid #ccc;padding:8px;text-align:left}</style></head><body>${target.outerHTML}</body></html>`);
          win.document.close();
          win.focus();
          win.print();
          toast('Print dialog opened for PDF export.', 'info');
        }
      });
    });
  };

  const initDateFormatter = () => {
    $$('time[data-date]').forEach((el) => {
      const iso = el.getAttribute('data-date');
      if (!iso) return;
      const date = new Date(iso);
      if (Number.isNaN(date.getTime())) return;
      const options = el.hasAttribute('data-date-time')
        ? { day: '2-digit', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' }
        : { day: '2-digit', month: 'short', year: 'numeric' };
      el.textContent = date.toLocaleDateString(undefined, options);
      el.setAttribute('datetime', iso);
    });
  };

  const initAutoRefresh = () => {
    const surface = $('[data-auto-refresh="true"]');
    if (!surface) return;

    window.setInterval(() => {
      if (document.visibilityState === 'visible') {
        window.location.reload();
      }
    }, 60000);
  };

  const init = () => {
    initTheme();
    initSidebar();
    initCounters();
    initFiltering();
    initCalendar();
    initConfirmations();
    initTableSort();
    initExport();
    initDateFormatter();
    initAlerts();
    initAutoRefresh();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.elmToast = toast;
})();
