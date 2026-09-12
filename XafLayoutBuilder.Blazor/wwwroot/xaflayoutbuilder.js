// Served by the Razor class library at _content/XafLayoutBuilder.Blazor/xaflayoutbuilder.js.
// A toolbar action runs on the server, and a browser will not start a download from there on its own:
// Chrome blocks top-level data: navigation, so an anchor has to be created and clicked. That is all this does.
export function downloadText(fileName, text, type = "text/plain;charset=utf-8") {
    const blob = new Blob([text], { type });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Give the browser a moment to start reading the blob before dropping it.
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
