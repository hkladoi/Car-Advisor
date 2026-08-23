FROM mcr.microsoft.com/playwright/python:v1.61.0-noble
WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/opt/venv/bin:${PATH}"
RUN apt-get update \
    && apt-get upgrade -y \
    && apt-get install --no-install-recommends -y python3.12-venv \
    && python -m venv /opt/venv \
    && rm -rf /var/lib/apt/lists/*
COPY workers/ingestion/requirements.txt ./requirements.txt
RUN python -m pip install --no-cache-dir --upgrade pip \
    && python -m pip install --no-cache-dir -r requirements.txt
COPY workers/ingestion/pyproject.toml ./pyproject.toml
COPY workers/ingestion/src ./src
COPY data ./data
RUN python -m pip install --no-cache-dir --no-deps .
RUN rm -rf \
        /root/.cache \
        /opt/venv/lib/python3.12/site-packages/pip \
        /opt/venv/lib/python3.12/site-packages/pip-*.dist-info \
        /usr/local/lib/python3.12/dist-packages/pip \
        /usr/local/lib/python3.12/dist-packages/pip-*.dist-info \
        /usr/local/lib/python3.12/dist-packages/virtualenv* \
        /usr/local/lib/python3.12/dist-packages/distlib* \
        /usr/local/lib/python3.12/dist-packages/filelock* \
        /usr/local/lib/python3.12/dist-packages/platformdirs* \
        /usr/local/lib/python3.12/dist-packages/python_discovery* \
        /usr/local/bin/pip* \
        /usr/local/bin/virtualenv \
    && apt-get purge -y python3.12-venv python3-pip-whl python3-setuptools-whl \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/* \
    && python -c "import ingestion.main, ingestion.scheduler, playwright"
RUN useradd --create-home --uid 10001 worker \
    && chown -R worker:worker /app /opt/venv
USER worker
CMD ["python", "-m", "ingestion.main"]
