/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        ink: '#0B0F14',
        panel: '#11161D',
        edge: '#1E2630',
      },
    },
  },
  plugins: [],
};
