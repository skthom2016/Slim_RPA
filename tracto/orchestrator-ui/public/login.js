'use strict';

const form   = document.getElementById('login-form');
const user   = document.getElementById('login-user');
const pass   = document.getElementById('login-pass');
const errBox = document.getElementById('login-error');
const submit = document.getElementById('login-submit');

document.getElementById('login-eye').addEventListener('click', () => {
    const show = pass.type === 'password';
    pass.type = show ? 'text' : 'password';
    document.getElementById('login-eye').title = show ? 'Hide the password' : 'Show the password';
});

form.addEventListener('submit', async e => {
    e.preventDefault();
    errBox.textContent = '';
    submit.disabled = true;
    try {
        const res = await fetch('/ui-auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: user.value.trim(), password: pass.value }),
        });
        const body = await res.json().catch(() => ({}));
        if (!res.ok) {
            errBox.textContent = body.message || 'Could not sign in';
            pass.value = '';
            pass.focus();
            return;
        }
        window.location.href = '/';
    } catch {
        errBox.textContent = 'Cannot reach the server';
    } finally {
        submit.disabled = false;
    }
});
