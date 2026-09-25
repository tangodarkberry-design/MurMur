/* =========================================================
   MurMur Server Monitor
   ========================================================= */

const MURMUR_SERVER_URL =
    " https://shaw-genius-ctrl-men.trycloudflare.com";

let redirecting = false;


/* =========================================================
   PssPss SignalR Connection
   ========================================================= */

let pssPssConnection = null;

if (
    typeof signalR !== "undefined" &&
    typeof signalR.HubConnectionBuilder !== "undefined"
) {

    pssPssConnection =
        new signalR.HubConnectionBuilder()
            .withUrl(
                `${MURMUR_SERVER_URL}/psspssHub`
            )
            .withAutomaticReconnect()
            .build();


    pssPssConnection.on(
        "ReceivePssPssMessage",
        async function (message) {

            console.log(
                "Received PssPss message:",
                message
            );

            if (
                typeof loadConversations ===
                "function"
            ) {
                await loadConversations();
            }

            if (
                typeof currentPssPssUser ===
                "undefined" ||
                !currentPssPssUser ||
                typeof addPssPssMessageBubble !==
                "function"
            ) {
                return;
            }

            const sender =
                message.senderUsername.toLowerCase();

            const receiver =
                message.receiverUsername.toLowerCase();

            const me =
                username.toLowerCase();

            const other =
                currentPssPssUser.toLowerCase();

            const isOurConversation =
                (
                    sender === me &&
                    receiver === other
                )
                ||
                (
                    sender === other &&
                    receiver === me
                );

            if (isOurConversation) {

                addPssPssMessageBubble(
                    message
                );

                if (
                    typeof markPssPssAsRead ===
                    "function"
                ) {
                    await markPssPssAsRead(
                        currentPssPssUser
                    );
                }
            }
        }
    );


    pssPssConnection.onreconnecting(
        function (error) {

            console.warn(
                "PssPss SignalR reconnecting:",
                error
            );
        }
    );


    pssPssConnection.onreconnected(
        function (connectionId) {

            console.log(
                "PssPss SignalR reconnected:",
                connectionId
            );
        }
    );


    pssPssConnection.onclose(
        function (error) {

            console.warn(
                "PssPss SignalR connection closed:",
                error
            );
        }
    );


    async function startPssPssConnection() {

        if (
            pssPssConnection.state ===
            signalR.HubConnectionState.Connected
        ) {
            return;
        }

        try {

            await pssPssConnection.start();

            console.log(
                "PssPss SignalR connected."
            );

        } catch (error) {

            console.error(
                "SignalR connection failed:",
                error
            );

            setTimeout(
                startPssPssConnection,
                5000
            );
        }
    }


    startPssPssConnection();

}
else {

    console.warn(
        "SignalR library was not loaded."
    );
}


/* =========================================================
   Check Server
   ========================================================= */

async function checkMurMurServer() {

    const controller =
        new AbortController();

    const timeout =
        setTimeout(() => {
            controller.abort();
        }, 1500);

    try {

        const response =
            await fetch(
                `${MURMUR_SERVER_URL}/api/status?t=${Date.now()}`,
                {
                    method: "GET",
                    cache: "no-store",
                    signal: controller.signal
                }
            );

        clearTimeout(timeout);

        if (!response.ok) {
            return false;
        }

        const data =
            await response.json();

        if (
            !data ||
            data.status !== "online"
        ) {
            return false;
        }

        return true;

    } catch {

        clearTimeout(timeout);

        return false;
    }
}


/* =========================================================
   Get Current Page
   ========================================================= */

function getCurrentPage() {

    return window.location.pathname
        .split("/")
        .pop()
        .toLowerCase();
}


/* =========================================================
   INDEX.HTML
   ========================================================= */

async function monitorIndex() {

    console.log(
        "Checking for MurMur server..."
    );

    const startTime =
        Date.now();

    const maxWait =
        10000;

    while (
        Date.now() - startTime <
        maxWait
    ) {

        const connected =
            await checkMurMurServer();

        if (connected) {

            console.log(
                "MurMur server found."
            );

            window.location.replace(
                "online.html"
            );

            return;
        }

        await new Promise(
            resolve =>
                setTimeout(
                    resolve,
                    500
                )
        );
    }


    console.log(
        "MurMur server not found."
    );

    window.location.replace(
        "offline.html"
    );
}


/* =========================================================
   OFFLINE.HTML
   ========================================================= */

async function monitorOffline() {

    const connected =
        await checkMurMurServer();

    if (connected) {

        console.log(
            "MurMur server is back."
        );

        window.location.replace(
            "online.html"
        );
    }
}


/* =========================================================
   ONLINE.HTML
   ========================================================= */

async function monitorOnline() {

    const connected =
        await checkMurMurServer();

    if (!connected) {

        console.log(
            "MurMur server disappeared."
        );

        window.location.replace(
            "offline.html"
        );

        return;
    }

    /*
     * Server is alive.
     *
     * Give online.html control to move
     * onto the login page.
     */
}


/* =========================================================
   EXISTING MURMUR PAGES
   ========================================================= */

async function monitorMurMurPage() {

    if (redirecting) {
        return;
    }

    const connected =
        await checkMurMurServer();

    if (!connected) {

        redirecting = true;

        console.log(
            "MurMur server went offline."
        );

        window.location.replace(
            "offline.html"
        );
    }
}


/* =========================================================
   Decide What To Monitor
   ========================================================= */

const currentPage =
    getCurrentPage();


if (
    currentPage === "" ||
    currentPage === "index.html"
) {

    /*
     * Loading screen.
     *
     * Search for the server for 10 seconds.
     */

    monitorIndex();

}
else if (
    currentPage === "offline.html"
) {

    /*
     * Offline page.
     *
     * Keep waiting for the server.
     */

    monitorOffline();

    setInterval(
        monitorOffline,
        1000
    );

}
else if (
    currentPage === "online.html"
) {

    /*
     * Online page.
     *
     * Double-check that the server
     * is actually alive.
     */

    monitorOnline();

}
else {

    /*
     * Login / main site / settings / etc.
     *
     * These pages monitor the server.
     */

    console.log(
        "MurMur server monitor started."
    );

    monitorMurMurPage();

    setInterval(
        monitorMurMurPage,
        500
    );
}