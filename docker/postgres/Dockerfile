FROM postgres:16

RUN apt-get update \
    && apt-get install -y --no-install-recommends openssl \
    && rm -rf /var/lib/apt/lists/*

COPY ssl-entrypoint.sh /usr/local/bin/ssl-entrypoint.sh
COPY pg_hba.conf /etc/postgresql/pg_hba.conf
RUN chmod +x /usr/local/bin/ssl-entrypoint.sh

ENTRYPOINT ["/usr/local/bin/ssl-entrypoint.sh"]
CMD ["postgres"]
