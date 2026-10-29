import { createRoot } from 'react-dom/client';
import FloodStudio from './features/dam-break';
import './features/dam-break/standalone.css';
createRoot(document.getElementById('root')!).render(<FloodStudio />);
