(function () {
    'use strict';

    window.cockpit = window.cockpit || {};

    const cockpit = window.cockpit;
    const DIFF_MARKERS = Object.freeze({
        escape: '\uE000',
        start: '\uE001',
        end: '\uE002'
    });
    const RESERVED_DIFF_MARKER_PATTERN = /[\uE000\uE001\uE002]/g;
    const SPLIT_SCROLL_HANDLER_KEY = '_cockpitSplitScrollHandler';
    const SPLIT_SCROLL_STATE_KEY = '_cockpitSplitScrollState';

    function logDiffWarning(message, error, details) {
        if (!window.console?.warn) {
            return;
        }

        if (details && error) {
            window.console.warn(`[cockpit.diff] ${message}`, details, error);
            return;
        }

        if (details) {
            window.console.warn(`[cockpit.diff] ${message}`, details);
            return;
        }

        if (error) {
            window.console.warn(`[cockpit.diff] ${message}`, error);
            return;
        }

        window.console.warn(`[cockpit.diff] ${message}`);
    }

    function resolveHighlightLanguage(highlighter, language) {
        return highlighter.getLanguage(language) ? language : 'plaintext';
    }

    function getDiffPrefixInfo(cell) {
        const prefixElement = cell.querySelector('.diff-prefix');
        const prefixText = prefixElement?.textContent ?? '';

        return {
            html: prefixElement?.outerHTML ?? '',
            textLength: prefixText.length
        };
    }

    function getRawCellText(cell, prefixTextLength) {
        return (cell.textContent ?? '').slice(prefixTextLength);
    }

    function parseDiffSpans(cell) {
        const serializedSpans = cell.getAttribute('data-diff-spans');
        if (!serializedSpans) {
            return [];
        }

        try {
            const parsedSpans = JSON.parse(serializedSpans);
            if (!Array.isArray(parsedSpans)) {
                logDiffWarning('Ignoring non-array diff span payload.', null, { serializedSpans });
                return [];
            }

            return parsedSpans;
        } catch (error) {
            logDiffWarning('Ignoring invalid data-diff-spans payload.', error, { serializedSpans });
            return [];
        }
    }

    function getCharacterHighlightClass(cell) {
        if (cell.classList.contains('added')) {
            return 'diff-char-added';
        }

        if (cell.classList.contains('removed')) {
            return 'diff-char-removed';
        }

        return '';
    }

    function toNonNegativeInteger(value) {
        const numericValue = Number(value);
        if (!Number.isFinite(numericValue)) {
            return null;
        }

        return Math.max(0, Math.trunc(numericValue));
    }

    function normalizeDiffRanges(textLength, spans) {
        if (textLength <= 0 || !Array.isArray(spans) || spans.length === 0) {
            return [];
        }

        const normalizedRanges = [];

        for (const span of spans) {
            if (!Array.isArray(span) || span.length < 2) {
                continue;
            }

            const start = toNonNegativeInteger(span[0]);
            const length = toNonNegativeInteger(span[1]);
            if (start === null || length === null || length === 0 || start >= textLength) {
                continue;
            }

            normalizedRanges.push({
                start,
                end: Math.min(start + length, textLength)
            });
        }

        if (normalizedRanges.length <= 1) {
            return normalizedRanges;
        }

        normalizedRanges.sort((left, right) => left.start - right.start || left.end - right.end);

        const mergedRanges = [normalizedRanges[0]];
        for (let index = 1; index < normalizedRanges.length; index += 1) {
            const currentRange = normalizedRanges[index];
            const previousRange = mergedRanges[mergedRanges.length - 1];
            if (currentRange.start <= previousRange.end) {
                previousRange.end = Math.max(previousRange.end, currentRange.end);
                continue;
            }

            mergedRanges.push(currentRange);
        }

        return mergedRanges;
    }

    function escapeReservedDiffMarkers(textSegment) {
        return textSegment.replace(RESERVED_DIFF_MARKER_PATTERN, (character) => `${DIFF_MARKERS.escape}${character}`);
    }

    function insertDiffMarkers(text, spans) {
        if (typeof text !== 'string' || text.length === 0) {
            return text ?? '';
        }

        const normalizedRanges = normalizeDiffRanges(text.length, spans);
        if (normalizedRanges.length === 0) {
            return escapeReservedDiffMarkers(text);
        }

        const parts = [];
        let cursor = 0;

        for (const range of normalizedRanges) {
            if (cursor < range.start) {
                parts.push(escapeReservedDiffMarkers(text.slice(cursor, range.start)));
            }

            parts.push(
                DIFF_MARKERS.start,
                escapeReservedDiffMarkers(text.slice(range.start, range.end)),
                DIFF_MARKERS.end
            );
            cursor = range.end;
        }

        if (cursor < text.length) {
            parts.push(escapeReservedDiffMarkers(text.slice(cursor)));
        }

        return parts.join('');
    }

    function renderHighlightedDiff(highlightedHtml, highlightClass) {
        const renderedParts = [];
        let isCharacterDiffOpen = false;

        for (let index = 0; index < highlightedHtml.length; index += 1) {
            const character = highlightedHtml[index];

            if (character === DIFF_MARKERS.escape) {
                const escapedCharacter = highlightedHtml[index + 1];
                if (
                    escapedCharacter === DIFF_MARKERS.escape ||
                    escapedCharacter === DIFF_MARKERS.start ||
                    escapedCharacter === DIFF_MARKERS.end
                ) {
                    renderedParts.push(escapedCharacter);
                    index += 1;
                    continue;
                }
            }

            if (character === DIFF_MARKERS.start) {
                if (highlightClass && !isCharacterDiffOpen) {
                    renderedParts.push(`<span class="${highlightClass}">`);
                    isCharacterDiffOpen = true;
                }
                continue;
            }

            if (character === DIFF_MARKERS.end) {
                if (highlightClass && isCharacterDiffOpen) {
                    renderedParts.push('</span>');
                    isCharacterDiffOpen = false;
                }
                continue;
            }

            renderedParts.push(character);
        }

        if (highlightClass && isCharacterDiffOpen) {
            renderedParts.push('</span>');
        }

        return renderedParts.join('');
    }

    function highlightDiffCell(highlighter, cell, highlightLanguage) {
        const prefix = getDiffPrefixInfo(cell);
        const rawText = getRawCellText(cell, prefix.textLength);
        const diffSpans = parseDiffSpans(cell);
        const markedText = insertDiffMarkers(rawText, diffSpans);

        try {
            const highlightedHtml = highlighter.highlight(markedText, {
                language: highlightLanguage,
                ignoreIllegals: true
            }).value;
            const characterHighlightClass = getCharacterHighlightClass(cell);
            cell.innerHTML = prefix.html + renderHighlightedDiff(highlightedHtml, characterHighlightClass);
        } catch (error) {
            logDiffWarning('Failed to highlight a diff cell.', error, {
                language: highlightLanguage,
                cellClassName: cell.className
            });
        }
    }

    cockpit.highlightDiffCells = function highlightDiffCells(containerId, language) {
        const highlighter = window.hljs;
        if (!highlighter) {
            return;
        }

        const container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        const highlightLanguage = resolveHighlightLanguage(highlighter, language);
        for (const cell of container.querySelectorAll('td.diff-cell')) {
            highlightDiffCell(highlighter, cell, highlightLanguage);
        }
    };

    cockpit._insertDiffMarkers = insertDiffMarkers;

    function clearPendingSplitScrollSync(state) {
        if (!state || state.frameId === null) {
            return;
        }

        window.cancelAnimationFrame(state.frameId);
        state.frameId = null;
        state.pendingSource = null;
        state.pendingTarget = null;
    }

    function removeSplitScrollHandler(element) {
        const existingHandler = element[SPLIT_SCROLL_HANDLER_KEY];
        if (!existingHandler) {
            return;
        }

        element.removeEventListener('scroll', existingHandler);
        delete element[SPLIT_SCROLL_HANDLER_KEY];
    }

    function clearSplitScrollState(element) {
        const state = element[SPLIT_SCROLL_STATE_KEY];
        if (!state) {
            return;
        }

        clearPendingSplitScrollSync(state);
        delete state.left?.[SPLIT_SCROLL_STATE_KEY];
        delete state.right?.[SPLIT_SCROLL_STATE_KEY];
        state.ignoredElement = null;
    }

    function flushPendingSplitScrollSync(state) {
        state.frameId = null;

        const source = state.pendingSource;
        const target = state.pendingTarget;
        state.pendingSource = null;
        state.pendingTarget = null;
        if (!source || !target) {
            return;
        }

        const nextScrollLeft = source.scrollLeft;
        const nextScrollTop = source.scrollTop;
        if (target.scrollLeft === nextScrollLeft && target.scrollTop === nextScrollTop) {
            return;
        }

        state.ignoredElement = target;
        target.scrollLeft = nextScrollLeft;
        target.scrollTop = nextScrollTop;

        window.requestAnimationFrame(() => {
            if (state.ignoredElement === target) {
                state.ignoredElement = null;
            }
        });
    }

    function queueSplitScrollSync(source, target, state) {
        if (state.ignoredElement === source) {
            return;
        }

        state.pendingSource = source;
        state.pendingTarget = target;
        if (state.frameId !== null) {
            return;
        }

        state.frameId = window.requestAnimationFrame(() => flushPendingSplitScrollSync(state));
    }

    function setSplitScrollHandler(source, target, state) {
        removeSplitScrollHandler(source);

        const handler = () => queueSplitScrollSync(source, target, state);
        source[SPLIT_SCROLL_HANDLER_KEY] = handler;
        source[SPLIT_SCROLL_STATE_KEY] = state;
        source.addEventListener('scroll', handler, { passive: true });
    }

    cockpit.setupSplitDiffScroll = function setupSplitDiffScroll(leftId, rightId) {
        const left = document.getElementById(leftId);
        const right = document.getElementById(rightId);
        if (!left || !right || left === right) {
            return;
        }

        removeSplitScrollHandler(left);
        removeSplitScrollHandler(right);
        clearSplitScrollState(left);
        clearSplitScrollState(right);

        const syncState = {
            left,
            right,
            frameId: null,
            pendingSource: null,
            pendingTarget: null,
            ignoredElement: null
        };

        setSplitScrollHandler(left, right, syncState);
        setSplitScrollHandler(right, left, syncState);
    };
})();
