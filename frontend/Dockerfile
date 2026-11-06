FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
COPY frontend/ frontend/
RUN npm ci --ignore-scripts && npm run build
FROM nginx:alpine
COPY frontend/nginx-main.conf /etc/nginx/nginx.conf
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/frontend/dist /usr/share/nginx/html
USER nginx
EXPOSE 8080
ENTRYPOINT ["nginx", "-g", "daemon off;"]
