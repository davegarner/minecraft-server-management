// Small JS helpers used by the Blazor components via IJSRuntime.
window.mcm = {
    scrollToBottom(element) {
        if (element) element.scrollTop = element.scrollHeight;
    },

    // The console input is handled entirely in the browser: the box is cleared the instant a command is
    // submitted, so fast typing can never be merged with a command still travelling to the server.
    attachConsoleInput(form, dotNetRef) {
        const input = form.querySelector("input");
        const history = [];
        let index = -1;

        form.addEventListener("submit", event => {
            event.preventDefault();
            const command = input.value.trim();
            if (!command || input.disabled) return;
            input.value = "";
            if (history[history.length - 1] !== command) history.push(command);
            index = -1;
            dotNetRef.invokeMethodAsync("SendFromBrowser", command);
        });

        input.addEventListener("keydown", event => {
            if (history.length === 0 || (event.key !== "ArrowUp" && event.key !== "ArrowDown")) return;
            event.preventDefault();
            if (event.key === "ArrowUp") {
                index = index < 0 ? history.length - 1 : Math.max(0, index - 1);
            } else if (index >= 0) {
                index++;
            }
            if (index >= history.length) index = -1;
            input.value = index < 0 ? "" : history[index];
        });
    },

    // navigator.clipboard requires HTTPS or localhost; fall back to execCommand for plain-HTTP LAN access.
    async copyText(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            const area = document.createElement("textarea");
            area.value = text;
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            let copied = false;
            try { copied = document.execCommand("copy"); } catch { /* not supported */ }
            area.remove();
            return copied;
        }
    },
};
