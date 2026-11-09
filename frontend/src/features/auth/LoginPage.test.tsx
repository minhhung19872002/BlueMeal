import { render, screen, act } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { AppProviders } from '@/app/providers';
import { changeLanguage } from '@/shared/i18n';
import LoginPage from './LoginPage';

function renderLogin() {
  return render(
    <AppProviders>
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    </AppProviders>,
  );
}

describe('LoginPage (bilingual)', () => {
  it('renders Vietnamese by default', async () => {
    await act(() => changeLanguage('vi'));
    renderLogin();
    expect(screen.getAllByText('Đăng nhập').length).toBeGreaterThan(0);
    expect(screen.getByText('Tên đăng nhập')).toBeInTheDocument();
    expect(screen.getByText('Quên mật khẩu?')).toBeInTheDocument();
  });

  it('switches to English instantly', async () => {
    await act(() => changeLanguage('en'));
    renderLogin();
    expect(screen.getAllByText('Sign in').length).toBeGreaterThan(0);
    expect(screen.getByText('Username')).toBeInTheDocument();
    await act(() => changeLanguage('vi'));
  });
});
