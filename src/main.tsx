import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import { EditingProvider } from './features/editing/context/EditingContext';
import './index.css';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <EditingProvider>
      <App />
    </EditingProvider>
  </React.StrictMode>
);
