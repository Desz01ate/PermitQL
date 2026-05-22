CREATE TABLE regions (
    region_id   integer PRIMARY KEY,
    region_name varchar(25)
);

CREATE TABLE countries (
    country_id   char(2) PRIMARY KEY,
    country_name varchar(60),
    region_id    integer REFERENCES regions(region_id)
);

CREATE TABLE locations (
    location_id     integer PRIMARY KEY,
    street_address  varchar(40),
    postal_code     varchar(12),
    city            varchar(30) NOT NULL,
    state_province  varchar(25),
    country_id      char(2) REFERENCES countries(country_id)
);

CREATE TABLE jobs (
    job_id      varchar(10) PRIMARY KEY,
    job_title   varchar(35) NOT NULL,
    min_salary  integer,
    max_salary  integer,
    CONSTRAINT jobs_salary_range_chk
        CHECK (min_salary IS NULL OR max_salary IS NULL OR min_salary <= max_salary)
);

CREATE TABLE departments (
    department_id    integer PRIMARY KEY,
    department_name  varchar(30) NOT NULL,
    manager_id       integer,
    location_id      integer REFERENCES locations(location_id)
);

CREATE TABLE employees (
    employee_id      integer PRIMARY KEY,
    first_name       varchar(20),
    last_name        varchar(25) NOT NULL,
    email            varchar(25) NOT NULL UNIQUE,
    phone_number     varchar(20),
    hire_date        date NOT NULL,
    job_id           varchar(10) NOT NULL REFERENCES jobs(job_id),
    salary           numeric(8,2),
    commission_pct   numeric(2,2),
    manager_id       integer REFERENCES employees(employee_id),
    department_id    integer REFERENCES departments(department_id),
    CONSTRAINT employees_salary_chk CHECK (salary IS NULL OR salary >= 0),
    CONSTRAINT employees_commission_chk
        CHECK (commission_pct IS NULL OR (commission_pct >= 0 AND commission_pct <= 0.99))
);

CREATE TABLE job_history (
    employee_id     integer NOT NULL REFERENCES employees(employee_id),
    start_date      date NOT NULL,
    end_date        date NOT NULL,
    job_id          varchar(10) NOT NULL REFERENCES jobs(job_id),
    department_id   integer REFERENCES departments(department_id),
    PRIMARY KEY (employee_id, start_date),
    CONSTRAINT job_history_dates_chk CHECK (end_date > start_date)
);

ALTER TABLE departments
    ADD CONSTRAINT departments_manager_fk
    FOREIGN KEY (manager_id) REFERENCES employees(employee_id)
    DEFERRABLE INITIALLY DEFERRED;

CREATE INDEX emp_department_ix ON employees(department_id);
CREATE INDEX emp_job_ix ON employees(job_id);
CREATE INDEX emp_manager_ix ON employees(manager_id);
CREATE INDEX dept_location_ix ON departments(location_id);
CREATE INDEX loc_country_ix ON locations(country_id);
CREATE INDEX country_region_ix ON countries(region_id);
CREATE INDEX jhist_employee_ix ON job_history(employee_id);
CREATE INDEX jhist_job_ix ON job_history(job_id);
