window.formPost = (url, data) => {
    const form = document.createElement('form');
    form.method = 'post';
    form.action = url;
    form.style.display = 'none';

    for (const key in data) {
        if (!Object.prototype.hasOwnProperty.call(data, key)) continue;
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = key;
        input.value = data[key];
        form.appendChild(input);
    }

    document.body.appendChild(form);
    form.submit();
};