import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50">
      <div class="card w-full max-w-md">
        <h1>Login</h1>
        <p class="text-sm text-neutral-600 mt-md">Email & password login (placeholder)</p>
      </div>
    </div>
  `
})
export class LoginComponent {}
