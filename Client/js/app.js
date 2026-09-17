/* =========================================================
   MurMur Server Monitor
   ========================================================= */

const MURMUR_SERVER_URL =
    "http://localhost:5175";

let redirecting = false;


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