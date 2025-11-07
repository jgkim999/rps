"use strict";

// SignalR connection for login functionality
var loginConnection = null;
var loginComponentReference = null;

// Set the component reference from Blazor
window.setLoginComponentReference = function (componentRef) {
    loginComponentReference = componentRef;
};

// Initialize SignalR connection
window.initializeSignalR = function () {
    if (loginConnection) {
        return; // Already initialized
    }

    loginConnection = new signalR.HubConnectionBuilder()
        .withUrl("/gamehub")
        .withAutomaticReconnect()
        .build();

    // Register event handlers
    loginConnection.on("OnLoginSuccess", function (userId, nickname, statistics) {
        if (loginComponentReference) {
            loginComponentReference.invokeMethodAsync('HandleLoginSuccess', userId, nickname, statistics);
        }
    });

    loginConnection.on("OnLoginFailed", function (errorMessage) {
        if (loginComponentReference) {
            loginComponentReference.invokeMethodAsync('HandleLoginFailed', errorMessage);
        }
    });

    // Handle connection closed
    loginConnection.onclose(function (error) {
        console.error("SignalR connection closed:", error);
    });
};

// Connect to the SignalR hub
window.connectToHub = async function () {
    if (!loginConnection) {
        throw new Error("SignalR connection not initialized. Call initializeSignalR first.");
    }

    try {
        await loginConnection.start();
        console.log("SignalR connected successfully");
    } catch (err) {
        console.error("Error connecting to SignalR:", err);
        throw err;
    }
};

// Invoke LoginUser hub method
window.loginUser = async function (nickname) {
    if (!loginConnection) {
        throw new Error("SignalR connection not initialized.");
    }

    if (loginConnection.state !== signalR.HubConnectionState.Connected) {
        throw new Error("SignalR connection is not in connected state.");
    }

    try {
        await loginConnection.invoke("LoginUser", nickname);
        console.log("LoginUser invoked successfully");
    } catch (err) {
        console.error("Error invoking LoginUser:", err);
        throw err;
    }
};
