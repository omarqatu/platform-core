import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { App } from './App';
import { I18nProvider } from './i18n/I18nProvider';
import { capturePendingInvitation, watchInvitationLinks } from './invitations/pendingInvitation';
import { capturePendingResetToken, watchResetLinks } from './password/pendingResetToken';
import { SessionProvider } from './session/SessionProvider';
import { applyInitialAppearance, ThemeProvider } from './theme/ThemeProvider';
import './theme/tokens.css';
import './index.css';

// Before anything renders: an invitation's token leaves the address at once, into memory (pendingInvitation),
// and a recovery token the same way (pendingResetToken).
capturePendingInvitation();
watchInvitationLinks();
capturePendingResetToken();
watchResetLinks();

// Before the first frame: the display mode and the accent on <html>, so no color flashes.
applyInitialAppearance();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <ThemeProvider>
        <BrowserRouter>
          <SessionProvider>
            <App />
          </SessionProvider>
        </BrowserRouter>
      </ThemeProvider>
    </I18nProvider>
  </StrictMode>,
);
