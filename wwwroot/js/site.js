(() => {
    "use strict";

    const form = document.getElementById("maturation-form");
    const dateDisplay = document.getElementById("hung-date-display");
    const dateValue = document.getElementById("hung-date-value");
    const dateError = document.getElementById("hung-date-client-error");
    const timeInput = document.getElementById("hung-time");
    const timeError = document.getElementById("hung-time-client-error");

    if (
        !form ||
        !dateDisplay ||
        !dateValue ||
        !dateError ||
        !timeInput ||
        !timeError
    ) {
        return;
    }

    const pad = value =>
        String(value).padStart(2, "0");

    const setFieldError = (input, errorElement, message) => {
        errorElement.textContent = message;
        errorElement.hidden = false;

        input.classList.add("input-validation-error");
        input.setAttribute("aria-invalid", "true");
    };

    const clearFieldError = (input, errorElement) => {
        errorElement.textContent = "";
        errorElement.hidden = true;

        input.classList.remove("input-validation-error");
        input.setAttribute("aria-invalid", "false");
    };

    const parseNorwegianDate = value => {
        const match = value.match(
            /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/);

        if (!match) {
            return null;
        }

        const day = Number(match[1]);
        const month = Number(match[2]);
        const year = Number(match[3]);

        const date = new Date(
            year,
            month - 1,
            day);

        const isValid =
            date.getFullYear() === year &&
            date.getMonth() === month - 1 &&
            date.getDate() === day;

        if (!isValid) {
            return null;
        }

        return {
            day,
            month,
            year
        };
    };

    const synchronizeDate = () => {
        const parsed = parseNorwegianDate(
            dateDisplay.value.trim());

        if (!parsed) {
            dateValue.value = "";

            setFieldError(
                dateDisplay,
                dateError,
                "Skriv ein gyldig dato som dd.mm.åååå.");

            return false;
        }

        dateDisplay.value =
            `${pad(parsed.day)}.` +
            `${pad(parsed.month)}.` +
            `${parsed.year}`;

        dateValue.value =
            `${parsed.year}-` +
            `${pad(parsed.month)}-` +
            `${pad(parsed.day)}`;

        clearFieldError(
            dateDisplay,
            dateError);

        return true;
    };

    const synchronizeTime = () => {
        const match = timeInput.value.match(
            /^(\d{1,2}):(\d{1,2})$/);

        if (!match) {
            setFieldError(
                timeInput,
                timeError,
                "Skriv klokkeslett som TT:mm.");

            return false;
        }

        const hour = Number(match[1]);
        const minute = Number(match[2]);

        if (hour > 23 || minute > 59) {
            setFieldError(
                timeInput,
                timeError,
                "Skriv eit gyldig klokkeslett.");

            return false;
        }

        timeInput.value =
            `${pad(hour)}:${pad(minute)}`;

        clearFieldError(
            timeInput,
            timeError);

        return true;
    };

    const formatDateInput = () => {
        let digits = dateDisplay.value
            .replace(/\D/g, "")
            .slice(0, 8);

        if (digits.length > 4) {
            digits =
                `${digits.slice(0, 2)}.` +
                `${digits.slice(2, 4)}.` +
                digits.slice(4);
        }
        else if (digits.length > 2) {
            digits =
                `${digits.slice(0, 2)}.` +
                digits.slice(2);
        }

        dateDisplay.value = digits;

        clearFieldError(
            dateDisplay,
            dateError);
    };

    const formatTimeInput = () => {
        let digits = timeInput.value
            .replace(/\D/g, "")
            .slice(0, 4);

        if (digits.length >= 3) {
            digits =
                `${digits.slice(0, 2)}:` +
                digits.slice(2);
        }

        timeInput.value = digits;

        clearFieldError(
            timeInput,
            timeError);
    };

    dateDisplay.addEventListener(
        "input",
        formatDateInput);

    dateDisplay.addEventListener(
        "blur",
        synchronizeDate);

    timeInput.addEventListener(
        "input",
        formatTimeInput);

    timeInput.addEventListener(
        "blur",
        synchronizeTime);

    form.addEventListener(
        "submit",
        event => {
            const dateIsValid = synchronizeDate();
            const timeIsValid = synchronizeTime();

            if (dateIsValid && timeIsValid) {
                return;
            }

            event.preventDefault();

            if (!dateIsValid) {
                dateDisplay.focus();
                return;
            }

            timeInput.focus();
        });

    synchronizeDate();
    synchronizeTime();
})();