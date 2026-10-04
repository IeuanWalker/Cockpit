(function () {
    window.cockpit = window.cockpit || {};
    window.cockpit.initializeWorkspaceDrag = function (sidebarId, helper) {
        const root = document.getElementById(sidebarId);
        if (!root) return;
        root.workspaceDragDispose?.();
        const listeners = new AbortController();
        const options = { capture: true, signal: listeners.signal };
        let drag = null;
        let suppressClickUntil = 0;
        let cursorState = null;

        function setDraggingCursor(active) {
            document.documentElement?.classList.toggle('workspace-tab-dragging', active);
            document.body.classList.toggle('workspace-tab-dragging', active);
            root.classList.toggle('workspace-tab-dragging', active);

            // WebView2 resolves the cursor from the element holding pointer capture.
            // Setting it directly on each possible cursor owner avoids the cursor
            // reverting to the arrow while the pointer crosses child WebViews/tools.
            if (active && !cursorState) {
                cursorState = [document.documentElement, document.body, root, drag?.tab]
                    .filter(element => element?.style)
                    .map(element => ({ element, value: element.style.getPropertyValue('cursor'),
                        priority: element.style.getPropertyPriority('cursor') }));
                for (const { element } of cursorState)
                    element.style.setProperty('cursor', 'grabbing', 'important');
            } else if (!active && cursorState) {
                for (const { element, value, priority } of cursorState) {
                    if (value) element.style.setProperty('cursor', value, priority);
                    else element.style.removeProperty('cursor');
                }
                cursorState = null;
            }
        }

        function clear() {
            const previous = drag;
            drag = null;
            previous?.preview?.remove();
            previous?.ghost?.remove();
            previous?.tab.classList.remove('dragging');
            setDraggingCursor(false);
            if (previous && root.hasPointerCapture(previous.pointerId)) root.releasePointerCapture(previous.pointerId);
        }

        function positionGhost(event) {
            if (!drag?.ghost) return;
            drag.ghost.style.left = event.clientX + 12 + 'px';
            drag.ghost.style.top = event.clientY + 12 + 'px';
        }

        function update(event) {
            drag.target = null;
            drag.preview.style.display = 'none';
            const element = document.elementFromPoint(event.clientX, event.clientY);
            const group = element?.closest('.workspace-group');
            if (!group || !root.contains(group)) return;
            const tab = element.closest('.workspace-tab');
            const strip = element.closest('.workspace-tabs');
            const target = { groupId: group.dataset.workspaceGroupId, anchorId: null, after: false, edge: null };
            let rect;
            if (strip) {
                const stripBounds = strip.getBoundingClientRect?.() || tab?.getBoundingClientRect();
                const visibleTabs = typeof strip.querySelectorAll === 'function'
                    ? Array.from(strip.querySelectorAll('.workspace-tab-list > .workspace-tab:not(.tab-overflowed)'))
                        .filter(candidate => candidate !== drag.tab)
                    : (tab && tab !== drag.tab ? [tab] : []);
                const slots = [];
                if (visibleTabs.length > 0) {
                    const tabBounds = visibleTabs.map(candidate => candidate.getBoundingClientRect());
                    slots.push({ x: tabBounds[0].left, anchorId: visibleTabs[0].dataset.workspaceInstanceId, after: false });
                    for (let index = 1; index < visibleTabs.length; index++) {
                        slots.push({ x: (tabBounds[index - 1].right + tabBounds[index].left) / 2,
                            anchorId: visibleTabs[index].dataset.workspaceInstanceId, after: false });
                    }
                    slots.push({ x: tabBounds.at(-1).right,
                        anchorId: visibleTabs.at(-1).dataset.workspaceInstanceId, after: true });
                }

                const slot = slots.reduce((nearest, candidate) =>
                    !nearest || Math.abs(candidate.x - event.clientX) < Math.abs(nearest.x - event.clientX)
                        ? candidate : nearest, null);
                if (slot) {
                    target.anchorId = slot.anchorId;
                    target.after = slot.after;
                }
                const insertionX = slot?.x ?? (stripBounds.left + 4);
                rect = { left: insertionX - 1.5, top: stripBounds.top, width: 3, height: stripBounds.height };
            } else {
                const content = group.querySelector('.workspace-tool-content');
                if (!content) return;
                const contentBounds = content.getBoundingClientRect();
                const surfaceBounds = root.querySelector('.workspace-surface').getBoundingClientRect();
                const x = (event.clientX - surfaceBounds.left) / surfaceBounds.width;
                const y = (event.clientY - surfaceBounds.top) / surfaceBounds.height;
                if (x < 0 || x > 1 || y < 0 || y > 1) return;
                rect = { left: contentBounds.left, top: contentBounds.top, width: contentBounds.width, height: contentBounds.height };
                const sectionCount = Number(group.dataset.sectionCount);
                const canSplit = !(sectionCount === 1 && drag.sourceCount === 1) &&
                    (sectionCount < 4 || drag.sourceCount === 1);
                if (canSplit) {
                    const edges = [['left', x], ['right', 1 - x], ['top', y], ['bottom', 1 - y]].sort((a, b) => a[1] - b[1]);
                    if (edges[0][1] < .22) {
                        target.edge = edges[0][0];
                        rect = { left: surfaceBounds.left, top: surfaceBounds.top, width: surfaceBounds.width, height: surfaceBounds.height };
                        if (target.edge === 'left' || target.edge === 'right') {
                            rect.width /= 2;
                            if (target.edge === 'right') rect.left += rect.width;
                        } else {
                            rect.height /= 2;
                            if (target.edge === 'bottom') rect.top += rect.height;
                        }
                    }
                }
            }
            drag.target = target;
            drag.preview.textContent = '';
            Object.assign(drag.preview.style, { left: rect.left + 'px', top: rect.top + 'px', width: rect.width + 'px', height: rect.height + 'px', display: 'grid' });
            positionGhost(event);
        }

        root.addEventListener('pointerdown', event => {
            const tab = event.target.closest('.workspace-tab');
            if (!tab || event.target.closest('.tab-close') || event.button !== 0 || !event.isPrimary) return;
            clear();
            const group = tab.closest('.workspace-group');
            drag = { tab, pointerId: event.pointerId, x: event.clientX, y: event.clientY,
                instanceId: tab.dataset.workspaceInstanceId, sourceGroup: group.dataset.workspaceGroupId,
                sourceCount: Number(group.dataset.tabCount), target: null };
        }, options);

        document.addEventListener('pointerdown', event => {
            if (root.querySelector('.tool-picker') && !event.target.closest?.('.tool-picker, .workspace-tab-add')) {
                helper.invokeMethodAsync('DismissWorkspaceToolPicker')
                    .catch(error => console.error('Workspace tool picker dismissal failed', error));
            }
            const contextMenuOpen = document.querySelector?.('.workspace-tab-context-menu');
            const openingAnotherContextMenu = event.button === 2 && event.target.closest?.('.workspace-tab');
            if (contextMenuOpen && !openingAnotherContextMenu && !event.target.closest?.('.workspace-tab-context-menu')) {
                helper.invokeMethodAsync('DismissWorkspaceTabContextMenu')
                    .catch(error => console.error('Workspace tab context menu dismissal failed', error));
            }
        }, options);

        root.addEventListener('pointermove', event => {
            if (!drag || event.pointerId !== drag.pointerId) return;
            if (!drag.preview) {
                if (Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 5) return;
                root.setPointerCapture(event.pointerId);
                const tabBounds = drag.tab.getBoundingClientRect();
                drag.ghost = drag.tab.cloneNode(true);
                drag.ghost.className = 'workspace-tab-ghost';
                drag.ghost.querySelector('.tab-close')?.remove();
                Object.assign(drag.ghost.style, { position: 'fixed', zIndex: '10002', pointerEvents: 'none',
                    boxSizing: 'border-box', width: tabBounds.width + 'px', height: tabBounds.height + 'px',
                    display: 'flex', alignItems: 'center', gap: '7px', padding: '0 8px', opacity: '.62',
                    border: '1px solid var(--border-color)', borderRadius: '5px', background: 'var(--hover-color)',
                    color: 'var(--text-color)', whiteSpace: 'nowrap', overflow: 'hidden' });
                drag.preview = document.createElement('div');
                drag.preview.className = 'workspace-pointer-preview';
                Object.assign(drag.preview.style, { position: 'fixed', zIndex: '10001', pointerEvents: 'none',
                    boxSizing: 'border-box', border: '2px solid #2488ff', borderRadius: '7px',
                    background: 'rgba(36,136,255,.13)', color: 'var(--text-color, white)',
                    placeItems: 'center', fontSize: '12px', fontWeight: '600' });
                document.body.appendChild(drag.preview);
                document.body.appendChild(drag.ghost);
                setDraggingCursor(true);
                drag.tab.classList.add('dragging');
                positionGhost(event);
            }
            event.preventDefault();
            update(event);
        }, options);

        window.addEventListener('pointerup', event => {
            if (!drag || event.pointerId !== drag.pointerId) return;
            const current = drag;
            if (current.preview) {
                update(event);
                suppressClickUntil = Date.now() + 400;
                event.preventDefault();
            }
            clear();
            if (current.preview && current.target) {
                const t = current.target;
                helper.invokeMethodAsync('DropWorkspaceTab', current.instanceId, t.groupId, t.anchorId, t.after, t.edge)
                    .catch(error => console.error('Workspace tab move failed', error));
            }
        }, options);
        root.addEventListener('dragstart', event => {
            if (event.target.closest('.workspace-tab')) event.preventDefault();
        }, options);
        root.addEventListener('click', event => {
            if (Date.now() < suppressClickUntil) { event.preventDefault(); event.stopImmediatePropagation(); }
        }, options);
        root.addEventListener('pointercancel', clear, options);
        root.addEventListener('lostpointercapture', clear, options);
        window.addEventListener('blur', clear, options);
        document.addEventListener('keydown', event => {
            if (event.key !== 'Escape') return;
            clear();
            if (document.querySelector?.('.workspace-tab-context-menu'))
                helper.invokeMethodAsync('DismissWorkspaceTabContextMenu').catch(() => {});
        }, options);
        root.workspaceDragDispose = () => { clear(); listeners.abort(); };
    };
})();
