'use strict';

// Felles, manuelt utløst kontroll. Resultater følger kilde + konfigurasjonsrevisjon og utløper etter ett minutt.
window.EndpointStatusControl = class {
  constructor(host, sourceId, revision) {
    this.host = host;
    this.sourceId = sourceId;
    this.revision = revision;
    this.host.classList.add('endpoint-status-control');
    this.status = document.createElement('span');
    this.status.className = 'endpoint-status';
    this.status.dataset.state = 'unknown';
    const light = document.createElement('span');
    light.className = 'endpoint-light'; light.setAttribute('aria-hidden', 'true');
    this.label = document.createElement('span'); this.label.textContent = 'Ikke sjekket';
    this.status.append(light, this.label);
    this.button = document.createElement('button');
    this.button.type = 'button'; this.button.className = 'secondary-button'; this.button.textContent = 'Test tilkobling';
    this.button.addEventListener('click', () => this.check());
    this.info = document.createElement('span'); this.info.className = 'endpoint-check-info';
    this.info.setAttribute('role', 'status'); this.info.setAttribute('aria-live', 'polite');
    this.host.append(this.status, this.button, this.info);
  }
  setDisabled(disabled, reason = '') {
    this.disabled = disabled;
    if (disabled) {
      this.cancel();
      this.status.dataset.state = 'unknown';
      this.label.textContent = reason ? 'Lagre først' : 'Ikke sjekket';
      this.info.textContent = reason;
    }
    this.button.disabled = disabled || !!this.request;
  }
  cancel() {
    this.request?.abort(); this.request = null;
    clearTimeout(this.expiry);
    this.button.textContent = 'Test tilkobling';
  }
  destroy() { this.destroyed = true; this.cancel(); }
  async check() {
    if (this.disabled || this.request || this.destroyed) return;
    clearTimeout(this.expiry);
    const request = new AbortController(); this.request = request;
    const deadline = setTimeout(() => request.abort(), 8000);
    this.button.disabled = true; this.button.textContent = 'Tester …';
    this.status.dataset.state = 'checking'; this.label.textContent = 'Sjekker …';
    this.info.textContent = 'Kontrollerer kontakt uten pasientoppslag.';
    try {
      const response = await fetch(`/api/sources/${encodeURIComponent(this.sourceId)}/status`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: request.signal,
        body: JSON.stringify({ configurationRevision: this.revision })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.issue?.map(i => i.details?.text).filter(Boolean).join(' ') || `Kontrollen feilet (HTTP ${response.status}).`);
      if (!['ok', 'warning', 'error'].includes(result.state) || !Number.isFinite(Date.parse(result.checkedAt)))
        throw new Error('Kontrollen ga et ugyldig svar. Prøv igjen.');
      if (this.request !== request || this.destroyed) return;
      this.status.dataset.state = result.state; this.label.textContent = result.label;
      const time = new Date(result.checkedAt).toLocaleTimeString('nb-NO', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
      this.info.textContent = `${result.detail} Sjekket kl. ${time}.`;
      this.expiry = setTimeout(() => {
        this.status.dataset.state = 'unknown'; this.label.textContent = 'Sjekk på nytt';
        this.info.textContent = `Sist sjekket kl. ${time}: ${result.label}. Statusen er over ett minutt gammel.`;
      }, Math.max(0, 60000 - (Date.now() - Date.parse(result.checkedAt))));
    } catch (error) {
      if (this.request !== request || this.destroyed) return;
      this.status.dataset.state = 'unknown'; this.label.textContent = 'Ikke sjekket';
      this.info.textContent = error.name === 'AbortError' ? 'Kontrollen tok for lang tid. Prøv igjen.' : error.message;
    } finally {
      clearTimeout(deadline);
      if (this.request === request) {
        this.request = null; this.button.disabled = !!this.disabled; this.button.textContent = 'Test tilkobling';
      }
    }
  }
};
