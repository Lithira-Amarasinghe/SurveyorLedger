import { Routes } from '@angular/router';
import { LoginComponent } from './pages/auth/login.component';
import { RegisterComponent } from './pages/auth/register.component';
import { WorkspaceComponent } from './pages/workspace/workspace.component';
import { ProfileComponent } from './pages/profile/profile.component';

export const routes: Routes = [
  {
    path: 'auth',
    children: [
      { path: 'login', component: LoginComponent },
      { path: 'register', component: RegisterComponent },
    ]
  },
  {
    path: 'app',
    children: [
      { path: 'workspace', component: WorkspaceComponent },
      { path: 'profile', component: ProfileComponent },
    ]
  },
  { path: '', redirectTo: '/auth/login', pathMatch: 'full' }
];
