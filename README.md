# Estudos sobre Observabilidade

## Executando Aplicação

```bash
cd Docker
docker compose up --build
```

### Recomendações

Se realizar modificações significativas no dockerfile, docker-compose ou qualquer outra modificação que julgue interessante limpar o ambiente pode usar o seguinte comando para limpar inclusive os volumes

```bash
docker compose down --volumes --rmi all && docker compose up --build   
```


## Filtro Grafana para exibir apenas os logs da aplicação

```bash
{service_name="observability/api"}
| json
|instrumentation_scope_name!~"Microsoft.Hosting.Lifetime|Microsoft.AspNetCore.Hosting.Diagnostics"
| line_format "{{.body}}"
```
