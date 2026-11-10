import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';
import '@/shared/i18n';
import '@fontsource/be-vietnam-pro/400.css';
import '@fontsource/be-vietnam-pro/500.css';
import '@fontsource/be-vietnam-pro/600.css';
import '@fontsource/be-vietnam-pro/700.css';
import './app/layout/layout.css';
import './app/layout/pages.css';
import './app/layout/pages-menus-ops.css';
import './index.css';
import './app/layout/pages-analysis-admin.css';
import { AppProviders } from '@/app/providers';
import { router } from '@/app/router';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
);
