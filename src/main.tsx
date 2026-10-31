import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import { EditingProvider } from './features/editing/context/EditingContext';
import { MediaEngineProvider } from './contexts/MediaEngineContext';
import { ProjectProvider } from './contexts/ProjectContext';
import './index.css';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <MediaEngineProvider>
      <ProjectProvider>
        <EditingProvider>
          <App />
        </EditingProvider>
      </ProjectProvider>
    </MediaEngineProvider>
  </React.StrictMode>
);
