"use strict";

// SignalR connection for lobby functionality
var lobbyConnection = null;
var lobbyComponentReference = null;

// Set the component reference from Blazor
window.setLobbyComponentReference = function (componentRef) {
    lobbyComponentReference = componentRef;
    initializeLobbyConnection();
};

// Initialize SignalR connection for lobby
function initializeLobbyConnection() {
    if (lobbyConnection) {
        return; // Already initialized
    }

    lobbyConnection = new signalR.HubConnectionBuilder()
        .withUrl("/gamehub")
        .withAutomaticReconnect()
        .build();

    // Register event handlers
    lobbyConnection.on("OnSkinSelected", function (skinId) {
        if (lobbyComponentReference) {
            lobbyComponentReference.invokeMethodAsync('HandleSkinSelected', skinId);
        }
    });

    lobbyConnection.on("OnSkinSelectionFailed", function (errorMessage) {
        if (lobbyComponentReference) {
            lobbyComponentReference.invokeMethodAsync('HandleSkinSelectionFailed', errorMessage);
        }
    });

    // Handle connection closed
    lobbyConnection.onclose(function (error) {
        console.error("SignalR connection closed:", error);
    });

    // Start connection
    lobbyConnection.start()
        .then(function () {
            console.log("Lobby SignalR connected successfully");
        })
        .catch(function (err) {
            console.error("Error connecting to SignalR in lobby:", err);
        });
}

// Invoke SelectSkin hub method
window.selectSkin = async function (userId, skinId) {
    if (!lobbyConnection) {
        throw new Error("SignalR connection not initialized.");
    }

    if (lobbyConnection.state !== signalR.HubConnectionState.Connected) {
        // Try to reconnect if not connected
        try {
            await lobbyConnection.start();
        } catch (err) {
            console.error("Failed to reconnect:", err);
            throw new Error("SignalR connection is not available.");
        }
    }

    try {
        await lobbyConnection.invoke("SelectSkin", userId, skinId);
        console.log("SelectSkin invoked successfully");
    } catch (err) {
        console.error("Error invoking SelectSkin:", err);
        throw err;
    }
};
