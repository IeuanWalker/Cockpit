const observers = new WeakMap();
const resizeSettleDelay = 90;

function isLiveElement(group) {
    return !!group &&
        (typeof Element === 'undefined' || group instanceof Element) &&
        group.isConnected === true &&
        typeof group.querySelector === 'function';
}

function measure(group) {
    if (!isLiveElement(group)) return;

    const strip = group.querySelector(':scope > .workspace-tabs');
    const list = strip?.querySelector(':scope > .workspace-tab-list');
    const tabs = list ? Array.from(list.querySelectorAll(':scope > .workspace-tab')) : [];
    if (!strip || !list || tabs.length === 0) return;

    strip.classList.remove('has-overflow');
    list.style.flex = '1 1 0px';
    tabs.forEach(tab => {
        tab.classList.remove('tab-overflowed');
        tab.style.width = '';
    });

    const gap = parseFloat(getComputedStyle(list).columnGap) || 0;
    const naturalWidths = tabs.map(tab => tab.getBoundingClientRect().width);
    const gapWidth = gap * Math.max(0, tabs.length - 1);
    const naturalTotal = naturalWidths.reduce((sum, width) => sum + width, 0) + gapWidth;
    let available = list.clientWidth;
    let appliedWidths = naturalWidths;
    let appliedTotal = naturalTotal;

    if (naturalTotal > available + 0.5) {
        const availableForTabs = Math.max(0, available - gapWidth);
        const naturalTabWidth = naturalWidths.reduce((sum, width) => sum + width, 0);
        const requestedScale = naturalTabWidth > 0 ? availableForTabs / naturalTabWidth : 1;
        const scale = Math.max(0.75, Math.min(1, requestedScale));
        appliedWidths = naturalWidths.map(width => width * scale);
        appliedTotal = appliedWidths.reduce((sum, width) => sum + width, 0) + gapWidth;
        tabs.forEach((tab, index) => tab.style.width = `${appliedWidths[index]}px`);

        if (appliedTotal > available + 0.5) {
            strip.classList.add('has-overflow');
            // The overflow control now consumes space. Re-read the flexible list
            // width before deciding which trailing tabs belong in its menu.
            list.style.flex = '1 1 0px';
            available = list.clientWidth;
        }
    }

    list.style.flex = `0 0 ${Math.min(appliedTotal, available)}px`;
    const positions = [];
    let position = 0;
    tabs.forEach((tab, index) => {
        if (index > 0) position += gap;
        positions.push({ start: position, end: position + appliedWidths[index] });
        position += appliedWidths[index];
    });

    if (strip.classList.contains('has-overflow')) {
        const activeIndex = tabs.findIndex(tab => tab.classList.contains('active'));
        if (activeIndex >= 0) {
            const active = positions[activeIndex];
            const viewportStart = list.scrollLeft || 0;
            const viewportEnd = viewportStart + available;
            if (active.start < viewportStart) list.scrollLeft = active.start;
            else if (active.end > viewportEnd) list.scrollLeft = Math.max(0, active.end - available);
        }
        const refreshOverflow = () => updateOverflow(strip, list, tabs, positions, available);
        list.onscroll = () => requestAnimationFrame(refreshOverflow);
        refreshOverflow();
    } else {
        list.onscroll = null;
        list.scrollLeft = 0;
        tabs.forEach(tab => tab.classList.remove('tab-overflowed'));
        updateMenu(strip, new Set());
    }
}

function updateOverflow(strip, list, tabs, positions, available) {
    const viewportStart = list.scrollLeft || 0;
    const viewportEnd = viewportStart + available;
    const overflowedIds = new Set();
    tabs.forEach((tab, index) => {
        const overflowed = positions[index].start < viewportStart - 0.5 || positions[index].end > viewportEnd + 0.5;
        tab.classList.toggle('tab-overflowed', overflowed);
        if (overflowed) overflowedIds.add(tab.dataset.workspaceInstanceId);
    });
    updateMenu(strip, overflowedIds);
}

function updateMenu(strip, overflowedIds) {
    strip.querySelectorAll('[data-workspace-overflow-instance-id]').forEach(item => {
        item.classList.toggle('menu-item-hidden', !overflowedIds.has(item.dataset.workspaceOverflowInstanceId));
    });
}

export function initialize(group) {
    // Recursive Blazor components can retain an ElementReference for one render
    // while a group is replaced by a split. Never pass that stale reference to
    // ResizeObserver; WebView2 resolves it as a non-Element value.
    if (!isLiveElement(group)) return;
    const previous = observers.get(group);
    previous?.observer.disconnect();
    clearTimeout(previous?.settleTimer);
    let settleTimer;
    const observer = new ResizeObserver(() => {
        // Measuring natural tab widths deliberately performs read/write layout work.
        // Wait until rapid sidebar/split resizing settles so active tool content can
        // resize smoothly without competing with repeated overflow calculations.
        clearTimeout(settleTimer);
        settleTimer = setTimeout(() => requestAnimationFrame(() => measure(group)), resizeSettleDelay);
    });
    observer.observe(group);
    observers.set(group, {
        observer,
        get settleTimer() { return settleTimer; }
    });
    requestAnimationFrame(() => measure(group));
    document.fonts?.ready.then(() => measure(group));
}

export function dispose(group) {
    if (!group || (typeof group !== 'object' && typeof group !== 'function')) return;
    const state = observers.get(group);
    state?.observer.disconnect();
    clearTimeout(state?.settleTimer);
    observers.delete(group);
}
