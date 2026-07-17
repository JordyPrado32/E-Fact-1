(function () {
    const menuSelector = '.row-actions-dropdown .action-dropdown-menu';
    let frameId = null;

    function schedulePosition() {
        if (frameId !== null) {
            return;
        }

        frameId = window.requestAnimationFrame(() => {
            frameId = null;
            positionMenus();
        });
    }

    function positionMenus() {
        const menus = document.querySelectorAll(menuSelector);
        menus.forEach((menu) => {
            const wrapper = menu.closest('.row-actions-dropdown');
            const trigger = wrapper ? wrapper.querySelector('.row-actions-trigger') : null;
            if (!trigger) {
                return;
            }

            menu.classList.add('is-floating-action-menu');
            menu.style.visibility = 'hidden';
            menu.style.setProperty('--floating-menu-top', '0px');
            menu.style.setProperty('--floating-menu-left', '0px');

            const triggerRect = trigger.getBoundingClientRect();
            const menuRect = menu.getBoundingClientRect();
            const gap = 8;
            const viewportPadding = 12;

            const spaceBelow = window.innerHeight - triggerRect.bottom - viewportPadding;
            const spaceAbove = triggerRect.top - viewportPadding;
            let top;

            if (spaceBelow >= menuRect.height || spaceBelow >= spaceAbove) {
                top = triggerRect.bottom + gap;
            } else {
                top = triggerRect.top - menuRect.height - gap;
            }

            top = Math.max(
                viewportPadding,
                Math.min(top, window.innerHeight - menuRect.height - viewportPadding)
            );

            let left = triggerRect.right - menuRect.width;
            left = Math.max(
                viewportPadding,
                Math.min(left, window.innerWidth - menuRect.width - viewportPadding)
            );

            menu.style.setProperty('--floating-menu-top', `${Math.round(top)}px`);
            menu.style.setProperty('--floating-menu-left', `${Math.round(left)}px`);
            menu.style.visibility = '';
        });
    }

    const observer = new MutationObserver(schedulePosition);

    function start() {
        observer.observe(document.body, { childList: true, subtree: true });
        schedulePosition();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }

    window.addEventListener('resize', schedulePosition, { passive: true });
    window.addEventListener('scroll', schedulePosition, { passive: true, capture: true });
})();
