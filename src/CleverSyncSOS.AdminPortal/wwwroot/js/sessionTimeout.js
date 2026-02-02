// Session Timeout Management
// FR-001: 30-minute session timeout with 5-minute warning

window.sessionTimeout = {
    timerId: null,
    warningTimerId: null,
    timeoutMinutes: 30,
    warningMinutes: 5,
    dotNetReference: null,
    lastActivityTime: null,

    // Initialize session timeout monitoring
    initialize: function (timeoutMinutes, warningMinutes, dotNetRef) {
        this.timeoutMinutes = timeoutMinutes;
        this.warningMinutes = warningMinutes;
        this.dotNetReference = dotNetRef;
        this.lastActivityTime = new Date();

        // Listen for user activity
        this.attachActivityListeners();

        // Start the timeout timer
        this.resetTimer();
    },

    // Attach event listeners for user activity
    attachActivityListeners: function () {
        const self = this;
        const events = ['mousedown', 'keydown', 'scroll', 'touchstart', 'click'];

        events.forEach(function (event) {
            document.addEventListener(event, function () {
                self.lastActivityTime = new Date();
            }, true);
        });
    },

    // Reset the session timeout timer
    resetTimer: function () {
        // Clear existing timers
        if (this.timerId) {
            clearTimeout(this.timerId);
        }
        if (this.warningTimerId) {
            clearTimeout(this.warningTimerId);
        }

        this.lastActivityTime = new Date();

        // Calculate timeout durations in milliseconds
        const timeoutDuration = this.timeoutMinutes * 60 * 1000;
        const warningDuration = (this.timeoutMinutes - this.warningMinutes) * 60 * 1000;

        const self = this;

        // Set timer to show warning
        this.warningTimerId = setTimeout(function () {
            if (self.dotNetReference) {
                self.dotNetReference.invokeMethodAsync('ShowWarning');
            }
        }, warningDuration);

        // Set timer for actual timeout (this will be cancelled if user extends session)
        this.timerId = setTimeout(function () {
            // Timeout reached - redirect to logout
            window.location.href = '/logout';
        }, timeoutDuration);

        console.log('Session timeout reset. Warning in ' + (warningDuration / 60000) + ' minutes, timeout in ' + (timeoutDuration / 60000) + ' minutes.');
    },

    // Cleanup on disposal
    dispose: function () {
        if (this.timerId) {
            clearTimeout(this.timerId);
            this.timerId = null;
        }
        if (this.warningTimerId) {
            clearTimeout(this.warningTimerId);
            this.warningTimerId = null;
        }
        this.dotNetReference = null;
    }
};
