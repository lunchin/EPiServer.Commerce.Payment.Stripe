var stripe;
var elements;
var StripePayment = {
    init: function () {
        stripe = Stripe('pk_test_AVagdVGZtVQCsjKtexKWpnua');
        elements = stripe.elements();
        var card = elements.create('card', {
            iconStyle: 'solid',
            style: {
                base: {
                    iconColor: '#c4f0ff',
                    color: '#000',
                    fontWeight: 500,
                    fontFamily: 'Roboto, Open Sans, Segoe UI, sans-serif',
                    fontSize: '15px',
                    fontSmoothing: 'antialiased',

                    ':-webkit-autofill': {
                        color: '#fce883',
                    },
                    '::placeholder': {
                        color: '#87BBFD',
                    },
                },
                invalid: {
                    iconColor: '#FFC7EE',
                    color: '#FFC7EE',
                },
            },
        });
        card.mount('#example1-card');
        StripePayment.registerElements([card]);
    },
    

    registerElements: function registerElements(elements) {
        var formClass = '.stripePayment';
        var example = document.querySelector(formClass);

        var form = document.querySelector('.jsCheckoutForm');
        var error = example.querySelector('.error');
        var errorMessage = error.querySelector('.message');

        function enableInputs() {
            Array.prototype.forEach.call(
                form.querySelectorAll(
                    "input[type='text']"
                ),
                function (input) {
                    input.removeAttribute('disabled');
                }
            );
        }

        function disableInputs() {
            Array.prototype.forEach.call(
                example.querySelectorAll(
                    "input[type='text']"
                ),
                function (input) {
                    input.setAttribute('disabled', 'true');
                }
            );
        }

        function triggerBrowserValidation() {
            // The only way to trigger HTML5 form validation UI is to fake a user submit
            // event.
            var submit = document.createElement('input');
            submit.type = 'submit';
            submit.style.display = 'none';
            form.appendChild(submit);
            submit.click();
            submit.remove();
        }

        // Listen for errors from each Element, and show error messages in the UI.
        var savedErrors = {};
        elements.forEach(function (element, idx) {
            element.on('change', function (event) {
                if (event.error) {
                    error.classList.add('visible');
                    savedErrors[idx] = event.error.message;
                    errorMessage.innerText = event.error.message;
                } else {
                    savedErrors[idx] = null;

                    // Loop over the saved errors and find the first one, if any.
                    var nextError = Object.keys(savedErrors)
                        .sort()
                        .reduce(function (maybeFoundError, key) {
                            return maybeFoundError || savedErrors[key];
                        }, null);

                    if (nextError) {
                        // Now that they've fixed the current error, show another one.
                        errorMessage.innerText = nextError;
                    } else {
                        // The user fixed the last error; no more errors.
                        error.classList.remove('visible');
                    }
                }
            });
        });

        // Listen on the form's 'submit' handler...
        form.addEventListener('submit', function (e) {
            e.preventDefault();

            // Trigger HTML5 validation UI on the form if any of the inputs fail
            // validation.
            var plainInputsValid = true;
            Array.prototype.forEach.call(form.querySelectorAll('input'), function (
                input
            ) {
                if (input.checkValidity && !input.checkValidity()) {
                    plainInputsValid = false;
                    return;
                }
            });
            if (!plainInputsValid) {
                triggerBrowserValidation();
                return;
            }

            // Show a loading screen...
            example.classList.add('submitting');

            // Disable all inputs.
            disableInputs();

            // Gather additional customer data we may have collected in our form.
            var name = form.querySelector('#stripe-name');
            var additionalData = {
                name: name ? name.value : undefined
            };

            // Use Stripe.js to create a token. We only need to pass in one Element
            // from the Element group in order to create a token. We can also pass
            // in the additional customer data we collected in our form.
            stripe.createToken(elements[0], additionalData).then(function (result) {
                // Stop loading!
                example.classList.remove('submitting');
                if (result.token) {
                    // If we received a token, show the token ID.
                    $('#Token').val(result.token.id);
                    $('#Type').val(result.token.card.brand);
                    $('#Month').val(result.token.card.exp_month);
                    $('#Year').val(result.token.card.exp_year);
                    $('#LastFour').val(result.token.card.last4);
                    $('#CustomerName').val(result.token.card.name);
                    var form = $('.jsCheckoutForm');
                    $.ajax({
                        type: "POST",
                        cache: false,
                        url: form[0].action,
                        data: form.serialize(),
                        success: function (result) {
                            window.location = result.Url;
                        }
                    });
                } else {
                    // Otherwise, un-disable inputs.
                    enableInputs();
                }
            });
        });
    }
};