# UI Implementation Guide (Phase 2)

## Pages to Implement

### Auth Flow (Public, no guards)

**Login** (`/auth/login`)
- Email input, password input, submit button
- Link to register page
- On success: redirect to `/app/workspace`, store JWT

**Register** (`/auth/register`)
- Email, password, firstName, lastName inputs
- Submit button, link to login
- API: POST /api/auth/register

**Verify OTP** (`/auth/verify-otp`)
- Email display, OTP code input (6 digits)
- Verify button
- API: POST /api/auth/verify-otp

### App Shell (layout wrapper for /app/*)

**Sidebar** (left, collapsible on mobile)
- Logo, navigation links (Workspace, Profile, Logout)
- Dark/light mode toggle (optional)

**Topbar** (fixed top)
- Logo/title, Cmd+K search input, user menu
- User dropdown: Profile link, Logout

**Command Palette** (Cmd+K modal)
- Route search (quick nav to pages)
- Fuzzy search across: Workspace, Profile

### Workspace (`/app/workspace`)

**List view**
- Table: workspace name, created date, action buttons
- Create button opens modal

**Create modal**
- Form: name input, description textarea
- API: POST /api/workspaces

### Profile (`/app/profile`)

**View section**
- Display: userId, email, firstName, lastName (read-only)

**Edit section** (tab or collapsible)
- Form: firstName, lastName inputs, save button
- API: PUT /api/users/profile

## Component Structure

```
ui/src/app/
├── pages/
│   ├── auth/
│   │   ├── login.component.ts
│   │   ├── register.component.ts
│   │   └── verify-otp.component.ts
│   ├── workspace/
│   │   ├── list/
│   │   ├── create-modal/
│   │   └── workspace.component.ts
│   └── profile/
│       ├── view/
│       ├── edit/
│       └── profile.component.ts
├── shell/
│   ├── sidebar.component.ts
│   ├── topbar.component.ts
│   ├── command-palette.component.ts
│   └── app-shell.component.ts
├── core/
│   ├── auth.service.ts
│   ├── http.interceptor.ts
│   └── auth.guard.ts
└── app.routes.ts
```

## Key Implementation Details

**HTTP + Auth**
- JwtInterceptor: Attach Bearer token to all requests
- AuthGuard: Redirect 401 to /auth/login
- AuthService: Login/register/logout, manage token in localStorage

**Styling**
- Tailwind utilities: btn-primary, btn-secondary, card, input-field
- Material components for tables, dialogs, dropdowns
- Palette: #9E0031 accent, #44001A-#8E0045 variants, neutral grays

**NOT in scope**: Surveys (beyond Land's basic survey/deed history), RBAC UI, org management, billing, password reset, social auth. Job list/detail UI is built — see docs/superpowers/specs/2026-08-09-job-ui-design.md.
