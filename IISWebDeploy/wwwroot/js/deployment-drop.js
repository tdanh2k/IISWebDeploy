// App-shell module: document delegation survives card rerenders and navigation.
for (const type of ["dragenter", "dragover", "dragleave", "drop"]) {
    document.addEventListener(type, event => {
        const card = event.target.closest?.("[data-deployment-drop]");
        if (!card) return;

        if (type === "dragleave") {
            if (!card.contains(event.relatedTarget)) card.classList.remove("drop-active");
            return;
        }
        if (!Array.from(event.dataTransfer?.types ?? []).includes("Files")) return;

        event.preventDefault(); // Also prevent file navigation on unavailable/busy cards.
        const input = card.querySelector('input[type="file"]:not(:disabled)');
        if (type === "drop") {
            card.classList.remove("drop-active");
            if (!input || !event.dataTransfer.files.length) return;
            // Read the native FileList before the browser protects the drag data store.
            input.files = event.dataTransfer.files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
        } else {
            event.dataTransfer.dropEffect = input ? "copy" : "none";
            card.classList.toggle("drop-active", !!input);
        }
    });
}
