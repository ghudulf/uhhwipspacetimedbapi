// Header scroll behaviour — dynamic thresholds based on actual page geometry
// - SCROLL_THRESHOLD: % of viewport height before hiding kicks in (default 15%)
// - HIDE_DELTA: % of viewport height scrolled down in one frame batch to trigger hide
// - SHOW_DELTA: % of viewport height scrolled up to trigger show
// All values recalculate on resize via ResizeObserver

(function () {
    'use strict';

    const logo = document.querySelector('.header-logo-panel');
    const controls = document.querySelector('.header-controls-panel');

    if (!logo || !controls) return;

    // Ratios relative to viewport height — feels natural on any screen size
    const THRESHOLD_RATIO = 0.04;  // hide after scrolling past just 4% of vh
    const HIDE_RATIO      = 0.008; // need to scroll down 0.8% of vh in one rAF batch

    let scrollThreshold, hideDelta;

    function recalc() {
        const vh = window.innerHeight;
        scrollThreshold = vh * THRESHOLD_RATIO;
        hideDelta        = vh * HIDE_RATIO;
    }

    recalc();

    // Recalculate on resize (orientation change, keyboard open/close on mobile)
    const ro = new ResizeObserver(recalc);
    ro.observe(document.documentElement);

    let lastScrollY = window.scrollY;
    let ticking = false;
    let logoHidden = false;

    function update() {
        const currentY = window.scrollY;
        const delta = currentY - lastScrollY;

        if (currentY < scrollThreshold) {
            // Back near top — show
            show();
        } else if (delta > hideDelta && !logoHidden) {
            hide();
        }
        // No mid-page show on scroll-up — header only comes back at top

        lastScrollY = currentY;
        ticking = false;
    }

    function hide() {
        logoHidden = true;
        logo.classList.add('header-hidden');
        controls.classList.add('header-compact');
    }

    function show() {
        logoHidden = false;
        logo.classList.remove('header-hidden');
        controls.classList.remove('header-compact');
    }

    window.addEventListener('scroll', function () {
        if (!ticking) {
            requestAnimationFrame(update);
            ticking = true;
        }
    }, { passive: true });

    // Swipe-up gesture → reveal header (avoids popping up on every tap/click)
    let touchStartY = 0;
    const SWIPE_UP_THRESHOLD = 20; // px of upward movement to count as intentional swipe

    document.addEventListener('touchstart', function (e) {
        touchStartY = e.touches[0].clientY;
    }, { passive: true });

    document.addEventListener('touchmove', function (e) {
        if (!logoHidden) return;
        const deltaY = e.touches[0].clientY - touchStartY;
        if (deltaY > SWIPE_UP_THRESHOLD) {
            // User is swiping down-the-finger (scrolling up the page) — reveal header
            show();
        }
    }, { passive: true });
})();