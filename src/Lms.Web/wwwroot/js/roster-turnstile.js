let loading;
const widgets = new WeakMap();

function load() {
    if (globalThis.turnstile) return Promise.resolve();
    if (!loading) {
        loading = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            const fail = () => {
                clearTimeout(timeout);
                script.remove();
                loading = undefined;
                reject(new Error("Security verification could not be loaded."));
            };
            const timeout = setTimeout(fail, 15000);
            script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
            script.async = true;
            script.onload = () => {
                clearTimeout(timeout);
                resolve();
            };
            script.onerror = fail;
            document.head.appendChild(script);
        });
    }
    return loading;
}

export async function render(element, siteKey) {
    await load();
    if (!element.isConnected || widgets.has(element)) return;
    const widget = { id: undefined, size: undefined, observer: undefined };
    const resize = () => {
        const size = element.getBoundingClientRect().width < 300 ? "compact" : "flexible";
        if (widget.size === size) return;
        if (widget.id !== undefined) globalThis.turnstile.remove(widget.id);
        widget.size = size;
        widget.id = globalThis.turnstile.render(element, {
            sitekey: siteKey,
            action: "roster-search",
            size,
            "response-field": false
        });
    };
    resize();
    widget.observer = new ResizeObserver(() => {
        if (element.isConnected) resize();
    });
    widget.observer.observe(element);
    widgets.set(element, widget);
}

export function takeToken(element) {
    const id = widgets.get(element)?.id;
    if (id === undefined) return "";
    const token = globalThis.turnstile.getResponse(id);
    // Each search needs a different single-use token, even if the search fails.
    globalThis.turnstile.reset(id);
    return token;
}

export function remove(element) {
    const widget = widgets.get(element);
    if (widget !== undefined) {
        widget.observer.disconnect();
        globalThis.turnstile.remove(widget.id);
        widgets.delete(element);
    }
}
