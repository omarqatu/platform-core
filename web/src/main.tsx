import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { App } from './App';
import { I18nProvider } from './i18n/I18nProvider';
import { capturePendingInvitation, watchInvitationLinks } from './invitations/pendingInvitation';
import { SessionProvider } from './session/SessionProvider';
import './index.css';

// Before anything renders: an invitation's token leaves the address at once, into memory (pendingInvitation).
capturePendingInvitation();
watchInvitationLinks();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <BrowserRouter>
        <SessionProvider>
          <App />
        </SessionProvider>
      </BrowserRouter>
    </I18nProvider>
  </StrictMode>,
);
