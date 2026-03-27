// Reconnect Modal Logic
const reconnectModal = document.getElementById("components-reconnect-modal");
const retryButton = document.getElementById("components-reconnect-button");
const resumeButton = document.getElementById("components-resume-button");

if (reconnectModal) {
    reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);
}

if (retryButton) {
    retryButton.addEventListener("click", retry);
}

if (resumeButton) {
    resumeButton.addEventListener("click", resume);
}

function handleReconnectStateChanged(event) {
    if (!reconnectModal) return;

    switch (event.detail.state) {
        case "show":
            if (!reconnectModal.open) {
                reconnectModal.showModal();
            }
            break;
        case "hide":
            if (reconnectModal.open) {
                reconnectModal.close();
            }
            break;
        case "failed":
            document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
            break;
        case "rejected":
            location.reload();
            break;
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        const successful = await Blazor.reconnect();
        if (!successful) {
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                location.reload();
            } else if (reconnectModal && reconnectModal.open) {
                reconnectModal.close();
            }
        }
    } catch (err) {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            location.reload();
        } else if (reconnectModal && reconnectModal.open) {
            reconnectModal.close();
        }
    } catch {
        location.reload();
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
